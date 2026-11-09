# ---------------------------------------------------------------------------------------------
# PostgreSQL: private network only, TLS 1.2+ required, encrypted at rest (platform-managed keys),
# 14-day point-in-time restore window, backups copied to the paired UK region.
# ---------------------------------------------------------------------------------------------

resource "random_password" "db" {
  length           = 40
  special          = true
  override_special = "_-."
}

resource "azurerm_postgresql_flexible_server" "main" {
  name                          = "psql-${local.name}"
  location                      = azurerm_resource_group.main.location
  resource_group_name           = azurerm_resource_group.main.name
  version                       = "17"
  sku_name                      = var.postgres_sku
  storage_mb                    = 32768
  auto_grow_enabled             = true
  backup_retention_days         = 14
  geo_redundant_backup_enabled  = var.postgres_geo_redundant_backup
  delegated_subnet_id           = azurerm_subnet.database.id
  private_dns_zone_id           = azurerm_private_dns_zone.postgres.id
  public_network_access_enabled = false
  administrator_login           = local.db_username
  administrator_password        = random_password.db.result
  tags                          = local.tags

  authentication {
    password_auth_enabled         = true
    active_directory_auth_enabled = false
  }

  maintenance_window {
    day_of_week  = 0
    start_hour   = 3
    start_minute = 0
  }

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [zone]
  }

  depends_on = [azurerm_private_dns_zone_virtual_network_link.postgres]
}

resource "azurerm_postgresql_flexible_server_configuration" "require_tls" {
  server_id = azurerm_postgresql_flexible_server.main.id
  name      = "require_secure_transport"
  value     = "on"
}

resource "azurerm_postgresql_flexible_server_configuration" "tls_version" {
  server_id = azurerm_postgresql_flexible_server.main.id
  name      = "ssl_min_protocol_version"
  value     = "TLSv1.2"
}

# Statement text can contain personal data; never log it.
resource "azurerm_postgresql_flexible_server_configuration" "no_statement_logging" {
  server_id = azurerm_postgresql_flexible_server.main.id
  name      = "log_statement"
  value     = "none"
}

resource "azurerm_postgresql_flexible_server_database" "couples" {
  name      = local.db_name
  server_id = azurerm_postgresql_flexible_server.main.id
  charset   = "UTF8"
  collation = "en_US.utf8"

  lifecycle {
    prevent_destroy = true
  }
}

# ---------------------------------------------------------------------------------------------
# Key Vault: the database connection string, the pilot code hash, and the certificate that
# encrypts the Data Protection key ring. RBAC only; soft delete and purge protection on.
# ---------------------------------------------------------------------------------------------

resource "azurerm_key_vault" "main" {
  name                          = "kv-${local.compact}"
  location                      = azurerm_resource_group.main.location
  resource_group_name           = azurerm_resource_group.main.name
  tenant_id                     = data.azurerm_client_config.current.tenant_id
  sku_name                      = "standard"
  rbac_authorization_enabled    = true
  purge_protection_enabled      = true
  soft_delete_retention_days    = 90
  public_network_access_enabled = length(var.operator_ip_ranges) > 0
  tags                          = local.tags

  network_acls {
    default_action = "Deny"
    bypass         = "None"
    ip_rules       = var.operator_ip_ranges
  }
}

resource "azurerm_private_endpoint" "vault" {
  name                = "pe-${local.name}-vault"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  subnet_id           = azurerm_subnet.vault.id
  tags                = local.tags

  private_service_connection {
    name                           = "vault"
    private_connection_resource_id = azurerm_key_vault.main.id
    subresource_names              = ["vault"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "vault"
    private_dns_zone_ids = [azurerm_private_dns_zone.vault.id]
  }
}

# Whoever runs Terraform writes the secrets; the apps can only read them.
resource "azurerm_role_assignment" "operator_secrets" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "operator_certificates" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Certificates Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "apps_secrets" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.apps.principal_id
}

resource "azurerm_key_vault_secret" "connection_string" {
  name         = "couples-connection-string"
  key_vault_id = azurerm_key_vault.main.id
  content_type = "Npgsql connection string"
  value = join(";", [
    "Host=${azurerm_postgresql_flexible_server.main.fqdn}",
    "Database=${local.db_name}",
    "Username=${local.db_username}",
    format("Password=%s", random_password.db.result),
    "SSL Mode=VerifyFull",
  ])

  depends_on = [azurerm_role_assignment.operator_secrets]
}

resource "azurerm_key_vault_secret" "pilot_code_hash" {
  name         = "pilot-access-code-sha256"
  key_vault_id = azurerm_key_vault.main.id
  value        = var.pilot_access_code_sha256

  depends_on = [azurerm_role_assignment.operator_secrets]
}

# Key Vault creates and keeps the private key; the app reads it as a base64 PKCS#12 secret. The
# certificate renews itself before expiry; see docs/operations/rollback-plan.md for rotation.
resource "azurerm_key_vault_certificate" "key_ring" {
  name         = "data-protection-key-ring"
  key_vault_id = azurerm_key_vault.main.id

  certificate_policy {
    issuer_parameters {
      name = "Self"
    }

    key_properties {
      exportable = true
      key_type   = "RSA"
      key_size   = 3072
      reuse_key  = false
    }

    secret_properties {
      content_type = "application/x-pkcs12"
    }

    x509_certificate_properties {
      subject            = "CN=bridgetogether-${var.environment_name}-key-ring"
      validity_in_months = 12
      key_usage          = ["keyEncipherment", "dataEncipherment"]
    }

    lifetime_action {
      action {
        action_type = "AutoRenew"
      }
      trigger {
        days_before_expiry = 60
      }
    }
  }

  depends_on = [azurerm_role_assignment.operator_certificates]
}
