# BridgeTogether staging on Azure (UK South). Written and validated only: nothing here has been
# applied. Applying creates billable resources and, if public_ingress_enabled is set, a public
# endpoint; both need the owner's go-ahead (see README.md in this folder).
terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.13"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.9"
    }
  }

  # State holds generated secrets (the database password, connection string). Keep it in a
  # private, encrypted storage account with versioning; supply the settings at init time:
  #   terraform init -backend-config=backend.hcl
  backend "azurerm" {}
}

provider "azurerm" {
  features {
    key_vault {
      purge_soft_delete_on_destroy = false
    }
    resource_group {
      prevent_deletion_if_contains_resources = true
    }
  }
}
