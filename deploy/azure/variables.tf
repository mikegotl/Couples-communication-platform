variable "location" {
  description = "Azure region. UK South keeps data in the UK; ukwest is the paired region."
  type        = string
  default     = "uksouth"
}

variable "environment_name" {
  description = "Short environment name used in resource names."
  type        = string
  default     = "staging"

  validation {
    condition     = can(regex("^[a-z0-9]{2,10}$", var.environment_name))
    error_message = "Use 2-10 lowercase letters or digits."
  }
}

variable "name_prefix" {
  description = "Prefix for resource names. Some names (Key Vault, registry) must be globally unique."
  type        = string
  default     = "bridgetogether"
}

variable "web_image" {
  description = "Web image reference in the registry, for example bridgetogether-web:1.0.0. Built by CI from deploy/docker."
  type        = string
}

variable "worker_image" {
  description = "Worker image reference in the registry, for example bridgetogether-worker:1.0.0."
  type        = string
}

variable "public_ingress_enabled" {
  description = "Expose the web app on the internet. Off by default; turning it on needs owner approval."
  type        = bool
  default     = false
}

variable "pilot_access_code_sha256" {
  description = "SHA-256 hex of the pilot invitation code (see docs/operations/pilot-readiness.md). Never the code itself."
  type        = string
  sensitive   = true

  validation {
    condition     = can(regex("^[0-9a-f]{64}$", var.pilot_access_code_sha256))
    error_message = "Expected 64 lowercase hex characters."
  }
}

variable "admin_emails" {
  description = "Accounts allowed into /admin pages."
  type        = list(string)
  default     = []
}

variable "operator_ip_ranges" {
  description = "CIDR ranges allowed to manage Key Vault secrets from outside Azure (for example the CI runner or an operator's address). The apps reach the vault privately."
  type        = list(string)
  default     = []
}

variable "otlp_endpoint" {
  description = "Optional OpenTelemetry collector endpoint for metrics and traces. Empty leaves telemetry export off; logs still go to Log Analytics."
  type        = string
  default     = ""
}

variable "postgres_sku" {
  description = "PostgreSQL Flexible Server SKU. Burstable is enough for a pilot of up to 60 people."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "postgres_geo_redundant_backup" {
  description = "Copy backups to the paired region (ukwest). Keeps data in the UK."
  type        = bool
  default     = true
}

variable "tags" {
  description = "Tags applied to every resource."
  type        = map(string)
  default = {
    application = "bridgetogether"
    environment = "staging"
    data        = "special-category"
  }
}
