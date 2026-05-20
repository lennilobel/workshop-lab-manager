# WorkshopLabManager

## Overview

`WorkshopLabManager` is a .NET console application for provisioning, publishing, managing, and distributing personalized Azure-based workshop lab environments for attendees.

The solution automates the end-to-end lifecycle of workshop infrastructure, including:

- Azure Virtual Machine cloning and deployment
- Azure Compute Gallery image publishing and regional replication
- Azure SQL Database provisioning
- Azure Event Hub namespace and SAS token creation
- Azure Storage Account provisioning
- Attendee resource tracking
- Automated attendee email delivery
- Resource listing and cleanup
- Multi-region VM distribution and capacity balancing

The application is designed for instructor-led technical workshops where every attendee requires isolated cloud resources.

---

# High-Level Architecture

The application is structured around several major concepts:

| Component | Responsibility |
|---|---|
| `Program.cs` | Application entry point and interactive console UI |
| `Context.cs` | Global runtime state container |
| `AppConfig.cs` | Strongly typed configuration model |
| `Managers/*` | High-level orchestration and workflows |
| `Helpers/*` | Azure resource helper utilities |
| `AttendeeInfo.cs` | Per-attendee resource metadata |
| `Attendees.csv` | Source attendee roster |
| `AttendeeResources.csv` | Generated attendee resource inventory |

The system uses:

- Azure Resource Manager SDK
- Azure CLI authentication
- Parallel asynchronous provisioning
- SMTP email delivery
- Azure Compute Gallery image replication

---

# Application Workflow

## Primary Lifecycle

The expected operational workflow is:

1. Configure application settings
2. Create or prepare source VM
3. Publish source VM image to Azure Compute Gallery
4. Replicate image to target Azure regions
5. Create attendee resources
6. Generate attendee resource inventory
7. Email attendees personalized credentials and endpoints
8. List resources as needed
9. Delete resources after workshop completion

---

# Console Menu System

The application exposes an interactive command-driven console UI.

## Main Menu Commands

| Command | Description |
|---|---|
| `V` | View configuration |
| `A` | Show attendee list |
| `L` | List attendee resources |
| `C` | Create attendee resources |
| `D` | Delete attendee resources |
| `E` | Email attendees |
| `P` | Publish VM image and replicate |
| `Q` | Quit |

Most commands optionally accept a specific attendee name.

Example:

```text
C JohnSmith
```

This provisions resources for only one attendee.

---

# Core Runtime Initialization

## `Program.InitializeApplication()`

Initialization performs:

### 1. Display startup banner

```csharp
Workshop Lab Manager
```

### 2. Load attendee list

Reads:

```text
Attendees.csv
```

Each row:

```csv
Name,Email
```

Blank lines and comment lines beginning with `#` are ignored.

### 3. Load configuration

Configuration sources:

- `appsettings.json`
- User secrets (optional)

### 4. Authenticate to Azure

Authentication method:

```csharp
AzureCliCredential
```

This requires:

```bash
az login
```

before application execution.

### 5. Resolve Azure subscription and resource groups

The application loads:

- Source resource group
- Target resource group
- Subscription metadata

### 6. Build shared runtime context

Stored in:

```csharp
Program.Context
```

---

# Configuration System

## `AppConfig.cs`

The configuration model is strongly typed and organized into logical sections.

---

## Root Configuration

| Property | Purpose |
|---|---|
| `WorkshopName` | Display name used in emails |
| `SourceResourceGroupName` | Resource group containing source VM |
| `TargetResourceGroupName` | Resource group receiving attendee resources |
| `TargetRegionName` | Primary Azure region |

---

# Virtual Machine Configuration

## `VirtualMachineConfig`

Controls VM publishing and attendee VM creation.

### Key Sections

- Publish configuration
- Clone configuration
- Credentials configuration

---

## Publish Configuration

### Purpose

Controls Compute Gallery image publishing and replication.

### Properties

| Property | Description |
|---|---|
| `SourceVmName` | Golden/source VM |
| `SnapshotName` | Snapshot resource name |
| `GalleryName` | Azure Compute Gallery name |
| `ImageName` | Shared image definition |
| `ImageVersion` | Gallery image version |
| `TargetRegionNames` | Replication target regions |

---

## Clone Configuration

### Purpose

Controls attendee VM deployment.

### Properties

| Property | Description |
|---|---|
| `VmSize` | Azure VM SKU |
| `MaxVmsPerRegion` | Capacity per region |
| `VmNamePrefix` | VM naming prefix |

---

## Credentials Configuration

### Purpose

Administrative VM login credentials.

### Properties

| Property | Description |
|---|---|
| `AdminUsername` | Local administrator |
| `AdminPassword` | VM password |

---

# SQL Database Configuration

## `SqlDatabaseConfig`

Controls Azure SQL provisioning.

### Features

- SQL Server creation
- Empty database creation
- AdventureWorks copy/import support

### Properties

| Property | Description |
|---|---|
| `ServerName` | SQL server naming prefix |
| `Username` | SQL admin username |
| `Password` | SQL admin password |
| `DatabaseSku` | Azure SQL SKU |
| `AdventureWorks.SourceServerName` | Source database server |
| `AdventureWorks.DatabaseName` | Source database name |

---

# Event Hub Configuration

## `EventHubConfig`

Controls Event Hub provisioning.

### Features

- Namespace creation
- Event Hub creation
- SAS token generation

### Properties

| Property | Description |
|---|---|
| `NamespaceName` | Namespace prefix |
| `EventHubName` | Event Hub name |
| `PolicyName` | SAS policy |
| `SasTokenExpirationDays` | SAS validity duration |

---

# Storage Configuration

## `StorageConfig`

Controls Azure Storage provisioning.

### Properties

| Property | Description |
|---|---|
| `AccountName` | Naming prefix |
| `ContainerName` | Blob container name |

---

# Email Configuration

## `EmailConfig`

Controls SMTP-based attendee email delivery.

### Properties

| Property | Description |
|---|---|
| `SmtpHost` | SMTP server |
| `SmtpPort` | SMTP port |
| `SmtpUsername` | SMTP username |
| `SmtpPassword` | SMTP password |
| `FromDisplayName` | Sender display name |
| `TestRecipient` | Override recipient |
| `EnableTestRecipient` | Enable email redirection |

---

# Manager Classes

# `ResourceCreationManager`

This is the primary orchestration engine for attendee provisioning.

---

## Major Responsibilities

- Parallelized provisioning
- Region balancing
- Capacity validation
- Resource inventory generation
- Resource orchestration

---

## Provisioning Flow

### 1. Validate regional capacity

Capacity formula:

```text
capacity = regions × maxVmsPerRegion
```

If attendees exceed capacity:

```csharp
throw new InvalidOperationException(...)
```

---

### 2. Assign attendees to regions

Attendees are distributed across configured Azure regions.

This prevents over-concentration of VMs in a single region.

---

### 3. Parallel provisioning

Uses:

```csharp
Parallel.ForEachAsync(...)
```

with:

```csharp
MaxDegreeOfParallelism = 10
```

This dramatically improves provisioning speed.

---

## Provisioned Resource Types

Depending on configuration flags:

| Resource Type | Optional |
|---|---|
| Virtual Machines | Yes |
| SQL Databases | Yes |
| Event Hubs | Yes |
| Storage Accounts | Yes |

---

## Output Generation

After provisioning:

```text
AttendeeResources.csv
```

is generated.

This file becomes the authoritative attendee resource inventory.

Columns include:

```csv
AttendeeName,
EmailAddress,
SqlDatabaseServerName,
EventHubNamespaceName,
EventHubSasToken,
StorageAccountConnectionString,
VirtualMachineIpAddress
```

---

# Virtual Machine Provisioning

## `VirtualMachineHelper`

Handles attendee VM deployment.

---

## Major Operations

### VNet/Subnet creation

Creates isolated networking resources.

### NSG creation

Automatically provisions RDP access rules.

### Public IP creation

Each attendee VM receives a public IP.

### NIC creation

Creates VM network interfaces.

### VM creation

Deploys VM from replicated gallery image.

---

## Naming Convention

VMs follow:

```text
vm-sql-hol-attendee-{identifier}
```

Public IPs:

```text
{vm-name}-pip
```

---

# Image Publishing System

# `VirtualMachinePublishManager`

This manager handles image lifecycle operations.

---

## 10-Step Publishing Workflow

### Step 1 — Discover Source VM

Retrieves source VM metadata.

---

### Step 2 — Deallocate VM

The VM must be stopped/deallocated before snapshot creation.

---

### Step 3 — Resolve OS Disk

Obtains managed OS disk ID.

---

### Step 4 — Create Snapshot

Creates managed disk snapshot using:

```csharp
DiskCreateOption.Copy
```

---

### Step 5 — Create/Get Azure Compute Gallery

Creates gallery if necessary.

---

### Step 6 — Create/Get Image Definition

Defines:

- OS type
- Hyper-V generation
- Trusted Launch support
- Publisher/Offer/SKU metadata

---

### Step 7 — Delete Existing Image Version

Ensures clean republishing.

---

### Step 8 — Publish New Image Version

Creates image version from snapshot.

---

### Step 9 — Replicate Across Regions

Replicates image to all configured regions.

---

### Step 10 — Cleanup and Completion

Reports total elapsed publishing time.

---

# Email Delivery System

# `EmailDeliveryManager`

Provides attendee communication automation.

---

## Email Workflow

### 1. Load attendee inventory

Reads:

```text
AttendeeResources.csv
```

---

### 2. Resolve current VM IPs

Fetches public IP addresses dynamically.

---

### 3. Build HTML email

Generates formatted HTML email content.

---

### 4. Send SMTP email

Uses:

```csharp
SmtpClient
```

with SSL enabled.

---

## Email Contents

The email may include:

- RDP connection information
- VM credentials
- SQL server information
- Event Hub SAS tokens
- Storage connection strings

---

## Test Recipient Feature

When enabled:

```json
"EnableTestRecipient": true
```

all emails are redirected to:

```json
"TestRecipient"
```

This is useful during testing.

---

# Resource Listing

# `ResourceListManager`

Provides inventory visibility.

---

## Responsibilities

- Enumerate attendee VMs
- Resolve public IP addresses
- Display attendee resource status

---

# Resource Deletion

# `ResourceDeletionManager`

Handles workshop cleanup.

---

## Responsibilities

- Delete attendee VMs
- Delete SQL servers/databases
- Delete Event Hub namespaces
- Delete Storage Accounts

---

## Safety Mechanisms

Deletion operations require explicit console confirmation.

---

# Helper Classes

# `ConsoleHelper`

Provides:

- Colored console output
- Yes/no confirmations
- User-friendly terminal formatting

---

# `SqlDatabaseHelper`

Provides:

- Database creation
- SQL execution helpers

---

# `StorageAccountHelper`

Provides:

- Storage naming logic
- Key retrieval helpers

---

# Azure Resource Strategy

## Source Resource Group

Contains:

- Golden VM
- Gallery resources
- Snapshots

---

## Target Resource Group

Contains attendee-specific resources.

---

# Regional Distribution Strategy

The application supports multi-region attendee deployment.

## Benefits

- Improved VM quota scaling
- Reduced regional bottlenecks
- Better workshop scalability
- Geographic resilience

---

# Concurrency Design

The provisioning engine uses asynchronous parallel execution.

## Benefits

- Faster attendee provisioning
- Better Azure API utilization
- Reduced workshop preparation time

## Risk Mitigation

The application intentionally limits concurrency:

```csharp
MaxDop = 10
```

to reduce:

- Azure throttling
- HTTP 429 responses
- Resource contention

---

# Security Considerations

## Authentication

Azure authentication uses:

```bash
az login
```

via:

```csharp
AzureCliCredential
```

---

## Secrets

Sensitive values should be stored in:

- User secrets
- Environment variables
- Secure configuration systems

Avoid committing secrets into source control.

---

## Trusted Launch

Gallery images enable:

```text
TrustedLaunch
```

improving VM security posture.

---

# File Inventory

| File | Purpose |
|---|---|
| `Program.cs` | Main application loop |
| `Context.cs` | Runtime context |
| `AppConfig.cs` | Configuration model |
| `AttendeeInfo.cs` | Attendee metadata |
| `Attendees.csv` | Input attendee list |
| `AttendeeResources.csv` | Generated attendee resources |
| `Managers/*.cs` | Orchestration logic |
| `Helpers/*.cs` | Utility logic |

---

# Build Requirements

## Required Software

- .NET SDK
- Azure CLI
- Azure subscription access

---

## Azure Permissions

The executing identity requires rights to:

- Create VMs
- Create snapshots
- Create Compute Gallery images
- Create networking resources
- Create SQL resources
- Create Event Hubs
- Create Storage Accounts

---

# Running the Application

## Authenticate

```bash
az login
```

---

## Build

```bash
dotnet build
```

---

## Run

```bash
dotnet run
```

---

# Operational Recommendations

## Before Workshop

- Validate Azure quotas
- Verify gallery replication
- Test email delivery
- Test attendee VM login
- Confirm regional capacity

---

## During Workshop

- Monitor Azure quotas
- Monitor VM provisioning
- Validate attendee connectivity

---

## After Workshop

- Export attendee inventory
- Archive logs
- Delete attendee resources
- Remove temporary snapshots/images

---

# Notable Design Strengths

## Strong Separation of Concerns

The solution cleanly separates:

- Orchestration
- Azure operations
- UI
- Configuration
- Utility helpers

---

## Scalable Provisioning

The region-balancing model allows scaling to larger workshops.

---

## Reusable Infrastructure

The Compute Gallery strategy avoids rebuilding attendee VMs individually.

---

## Automation-Friendly

The architecture is highly automatable and suitable for:

- Training events
- Hackathons
- Labs
- Certification workshops
- Internal enablement programs

---

# Conclusion

`WorkshopLabManager` is a sophisticated Azure workshop automation platform that significantly reduces the operational overhead of provisioning personalized lab environments.

The application combines:

- Azure Compute Gallery publishing
- Parallelized infrastructure provisioning
- Multi-region VM scaling
- Automated attendee communications
- Resource lifecycle management

into a cohesive and production-oriented workshop operations toolkit.
