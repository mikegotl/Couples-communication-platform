# ---------------------------------------------------------------------------------------------
# Container Apps: one web replica (Blazor Server circuits are held in memory, so a second replica
# would need sticky sessions) and one worker. Both run as non-root from chiseled images.
# ---------------------------------------------------------------------------------------------

resource "azurerm_container_app_environment" "main" {
  name                           = "cae-${local.name}"
  location                       = azurerm_resource_group.main.location
  resource_group_name            = azurerm_resource_group.main.name
  log_analytics_workspace_id     = azurerm_log_analytics_workspace.main.id
  infrastructure_subnet_id       = azurerm_subnet.apps.id
  internal_load_balancer_enabled = !var.public_ingress_enabled
  tags                           = local.tags

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }
}

locals {
  # Secrets come from Key Vault through the apps' identity; nothing secret is set here.
  vault_secrets = {
    "connection-string" = azurerm_key_vault_secret.connection_string.versionless_id
    "pilot-code-hash"   = azurerm_key_vault_secret.pilot_code_hash.versionless_id
    "key-ring-pfx"      = azurerm_key_vault_certificate.key_ring.versionless_secret_id
  }

  common_env = {
    ASPNETCORE_ENVIRONMENT      = "Production"
    Observability__OtlpEndpoint = var.otlp_endpoint
    Database__RequireTls        = "true"
  }

  web_env = merge(local.common_env, {
    # One replica applies pending migrations at start-up; see docs/operations/rollback-plan.md.
    Database__MigrateOnStartup                 = "true"
    DataProtection__CertificatePath            = "/mnt/secrets/key-ring-pfx"
    ForwardedHeaders__Enabled                  = "true"
    ForwardedHeaders__KnownNetworks__0         = local.apps_cidr
    FeatureManagement__PilotEnrollmentRequired = "true"
    FeatureManagement__Phase2JointChat         = "false"
    Exercises__RequireClinicianApproval        = "true"
  })
}

resource "azurerm_container_app" "web" {
  name                         = "ca-${local.name}-web"
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  dynamic "secret" {
    for_each = local.vault_secrets
    content {
      name                = secret.key
      key_vault_secret_id = secret.value
      identity            = azurerm_user_assigned_identity.apps.id
    }
  }

  ingress {
    external_enabled           = true
    target_port                = 8080
    transport                  = "auto"
    allow_insecure_connections = false

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    volume {
      name         = "secrets"
      storage_type = "Secret"
    }

    container {
      name   = "web"
      image  = "${azurerm_container_registry.main.login_server}/${var.web_image}"
      cpu    = 0.5
      memory = "1Gi"

      dynamic "env" {
        for_each = local.web_env
        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = { for i, email in var.admin_emails : "Administration__AdminEmails__${i}" => email }
        content {
          name  = env.key
          value = env.value
        }
      }

      env {
        name        = "ConnectionStrings__Couples"
        secret_name = "connection-string"
      }

      env {
        name        = "Pilot__AccessCodeSha256"
        secret_name = "pilot-code-hash"
      }

      volume_mounts {
        name = "secrets"
        path = "/mnt/secrets"
      }

      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health"
      }

      readiness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/ready"
      }
    }
  }

  depends_on = [azurerm_role_assignment.apps_pull, azurerm_role_assignment.apps_secrets]
}

resource "azurerm_container_app" "worker" {
  name                         = "ca-${local.name}-worker"
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  secret {
    name                = "connection-string"
    key_vault_secret_id = azurerm_key_vault_secret.connection_string.versionless_id
    identity            = azurerm_user_assigned_identity.apps.id
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "worker"
      image  = "${azurerm_container_registry.main.login_server}/${var.worker_image}"
      cpu    = 0.25
      memory = "0.5Gi"

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.key
          value = env.value
        }
      }

      env {
        name        = "ConnectionStrings__Couples"
        secret_name = "connection-string"
      }
    }
  }

  depends_on = [azurerm_role_assignment.apps_pull, azurerm_role_assignment.apps_secrets]
}
