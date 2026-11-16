# EMB Production

An ERP system for apparel/garment production — covering work orders, production tracking, quality control, inventory, purchasing, and dispatch — built as an ASP.NET MVC 5 backend hosting an Angular single-page application.

> **Note:** This repository is public so the source can be viewed and reviewed, but it is **not open source** — see [License](#license) below. No license is granted to copy, modify, distribute, or otherwise use this code without prior written permission from the copyright holder.

## Tech stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET MVC 5, .NET Framework 4.7.2, C# |
| Data access | Entity Framework 6 (Database-First, EDMX) |
| Database | SQL Server |
| Frontend | Angular 20 (standalone components), TypeScript |
| UI | Bootstrap 5, Angular Material, Bootstrap Icons |
| Reporting / exports | ClosedXML, EPPlus, Rotativa (PDF), jsPDF |

## Repository layout

```
EMB_Production 2/
├── EMB_Production 2.sln              # Visual Studio solution
└── EMB_Production 2/                 # Web project (root namespace: EMB_Production)
    ├── Controllers/                  # MVC + JSON API controllers
    ├── Models/                       # EF6 Database-First entities (Model1.edmx)
    ├── ViewModels/                   # DTOs returned to the Angular app
    ├── App_Start/                    # Routing, bundling, filter config
    ├── Infrastructure/               # Shared JSON result helpers, etc.
    ├── Content/emb-products/         # Built Angular bundle (served by the MVC app)
    └── ClientApp/emb-products/       # Angular CLI workspace (source for the SPA)
Reports/                              # Project documentation / progress reports
```

## Core modules

- **Work Orders & Job Orders** — order intake through production planning
- **Daily Production** — shop-floor output, machine/operator tracking, stoppage logging
- **QC Inspection** — batch-level quality inspection with a rework/re-inspection loop
- **Inventory & Stock** — unified stock ledger (`Gen_Stocks`) for raw materials and finished goods
- **Purchasing** — purchase requisitions, purchase orders, supplier management
- **Dispatch** — finished-goods dispatch against customer orders
- **Admin** — users, roles, branches, company profile

## Getting started

### Prerequisites

- Visual Studio 2019+ with the ASP.NET and web development workload
- SQL Server (the project targets `localhost\SQLEXPRESS03` by default — update the connection strings in `Web.config` to match your instance)
- Node.js + npm (for the Angular frontend)

### Backend

1. Open `EMB_Production 2.sln` in Visual Studio.
2. Restore NuGet packages (`packages.config`-based restore).
3. Update the `EMB_ProductionEntities` / `EMB_ProductionEntities1` connection strings in `Web.config` to point at your SQL Server instance and database.
4. Build and run (IIS Express).

### Frontend

The Angular app builds directly into `Content/emb-products/`, which the MVC app serves as a static SPA.

```powershell
cd "EMB_Production 2/ClientApp/emb-products"
npm install
npm start              # ng serve, for local Angular-only development
npm run build:deploy   # production build, copied into Content/emb-products
```

For full-stack development, run `ng serve` on port 4200 alongside the MVC site (IIS Express) — the Angular `environment.ts` points API calls at the MVC site's URL.

## Architecture notes

- The MVC app serves the Angular SPA shell for any non-API route (`HomeController.Angular()`), while JSON API endpoints use attribute routing under `emb-products/<area>` (e.g. `emb-products/qc-inspection`, `emb-products/dropdowns`).
- All API controllers derive from `BaseController`, which centralizes the shared `DbContext`, current-user/branch resolution, and the standard `{ success, message, data }` JSON response envelope.
- Most transactional records (work orders, QC inspections, daily production, dispatches) follow a draft → posted → reversed lifecycle, and use soft deletes rather than hard deletes.

## License

**Source-available — all rights reserved.** This repository is public for visibility/review purposes only. Viewing the code does not grant any license to use, copy, modify, merge, publish, distribute, sublicense, or sell copies of this software, in whole or in part. All rights are reserved by the copyright holder. For permission requests, contact the repository owner.
