output "web_url" {
  description = "The web app's address (internal to the virtual network unless public_ingress_enabled)."
  value       = "https://${azurerm_container_app.web.ingress[0].fqdn}"
}

output "registry_login_server" {
  description = "Push images here (az acr login, then docker push)."
  value       = azurerm_container_registry.main.login_server
}

output "key_vault_name" {
  value = azurerm_key_vault.main.name
}

output "postgres_server_name" {
  description = "Used for point-in-time restore (docs/operations/backup-policy.md)."
  value       = azurerm_postgresql_flexible_server.main.name
}
