import { Routes } from '@angular/router';
import { HomeComponent } from './home/home.component';
import { LoginComponent } from './auth/login.component';
import { LayoutComponent } from './layout/layout.component';
import { DashboardComponent } from './dashboard/dashboard.component';
import { AuthGuard } from './auth/auth.guard';


// Admin
import { AdmUsersComponent } from './adm-user/adm-user.component';
import { AdmBranchComponent } from './adm-branch/adm-branch.component';
import { AdmCompanyComponent } from './adm-company/adm-company.component';
import { HrCustomerComponent } from './customers/hr-customer.component';
import { GenSuppliersComponent } from './gen-suppliers/gen-suppliers.component';

//Designs & Patterns
import { GenDesignComponent } from './design/gen-design.component';
import { DesignCostingComponent } from './designcosting/design-costing';
import { GenComponentsComponent } from './components/gen-components.component';
import { ComponentMaterialsComponent } from './component-material/component-materials';


//Operations
import { WorkOrderComponent } from './work-order/work-order.component';
import { JobOrdersComponent } from './job-orders/job-orders.component';
import { WorkOrderItemsComponent } from './work-order-items/work-order-items.component';
import { JobTrackingHistoryComponent } from './job-tracking-history/job-tracking-history.component';
import { DispatchComponent } from './dispatch/dispatch.component';
import { DispatchItemsComponent } from './dispatch-items/dispatch-items.component';


//Production
import { ProductionLedgerComponent } from './production-ledger/production-ledger';
import { DailyProductionComponent } from './daily-production/daily-production.component';
import { DailyProductionStopsComponent } from './daily-production-stops/daily-production-stops.component';
import { GenMachinesComponent } from './gen-machines/gen-machines.component';
import { GenMachineParametersComponent } from './gen-machine-parameters/gen-machine-parameters.component';
import { MachineCostProfileComponent } from './machine-cost-profile/machine-cost-profile';
import { QcInspectionComponent } from './qc-inspection/qc-inspection';
import { QcInspectionItemsComponent } from './qc-inspection-items/qc-inspection-items';
import { FinishedStockComponent } from './finished-stock/finished-stock.component';

//Products & Inventory
import { GenProductComponent } from './gen-product/gen-product.component';
import { GenThreadsComponent } from './gen-threads/gen-threads.component';
import { GenBrandComponent } from './brands/gen-brand.component';
import { GenCategoryComponent } from './categories/gen-category.component';
import { GenStocksComponent } from './gen-stocks/gen-stocks.component';
import { GenStoreComponent } from './gen-store/gen-store';
import { PurchaseComponent } from './purchase/purchase.component';
import { GenBomComponent } from './bom/gen-bom.component';

//Services & Rates
import { GenServicesComponent } from './gen-services/gen-services.component';
import { ServiceTypeComponent } from './servicetype/servicetype.component';
import { GenServiceRatesComponent } from './gen-service-rates/gen-service-rates.component';


//Settings




export const routes: Routes = [

  { path: '', component: HomeComponent },
  { path: 'login', component: LoginComponent },
   
  {
    path: '',
    component: LayoutComponent,
    canActivate: [AuthGuard],
    children: [

      { path: 'dashboard', component: DashboardComponent },

      /** ------------------------------------------------------
       *  ADMIN MODULES
       * ------------------------------------------------------ */
      { path: 'users', component: AdmUsersComponent },
      { path: 'branches', component: AdmBranchComponent },
      { path: 'companies', component: AdmCompanyComponent },
      { path: 'customers', component: HrCustomerComponent },
      { path: 'suppliers', component: GenSuppliersComponent },
      {
        path: 'suppliers/:id/profile',
        loadComponent: () => import('./gen-suppliers/supplier-profile.component').then(m => m.SupplierProfileComponent)
      },

      //Designs & Patterns
      { path: 'components', component: GenComponentsComponent },
      { path: 'component-materials', component: ComponentMaterialsComponent },
      { path: 'designs', component: GenDesignComponent },
      {
        path: 'designs/:id/profile',
        loadComponent: () => import('./design/design-profile.component').then(m => m.DesignProfileComponent)
      },
      { path: 'design-costing', component: DesignCostingComponent },

      //Operations
      { path: 'work-orders', component: WorkOrderComponent },
      { path: 'job-orders', component: JobOrdersComponent },
      { path: 'work-order-items', component: WorkOrderItemsComponent },
      { path: 'job-tracking-history', component: JobTrackingHistoryComponent },
      { path: 'job-tracking-history/:workOrderItemId', component: JobTrackingHistoryComponent },
      { path: 'dispatch', component: DispatchComponent },
      { path: 'dispatch/:dispatchId/items', component: DispatchItemsComponent },


      /** ------------------------------------------------------
       *  PRODUCTION
       * ------------------------------------------------------ */
      { path: 'production-ledger', component: ProductionLedgerComponent },
      {
        path: 'production-plan',
        loadComponent: () => import('./production-plan/production-plan.component').then(m => m.ProductionPlanComponent)
      },
      { path: 'daily-production', component: DailyProductionComponent },
      { path: 'daily-production-stops', component: DailyProductionStopsComponent },
      { path: 'qc-inspection', component: QcInspectionComponent },
      { path: 'qc-inspection-items', component: QcInspectionItemsComponent },
      { path: 'finished-stock', component: FinishedStockComponent },

      //Products & Inventory
      { path: 'brands', component: GenBrandComponent },
      { path: 'products', component: GenProductComponent },
      { path: 'categories', component: GenCategoryComponent },
      { path: 'threads', component: GenThreadsComponent },
      { path: 'bom', component: GenBomComponent },
      { path: 'machines', component: GenMachinesComponent },
      { path: 'machine-parameters', component: GenMachineParametersComponent },
      { path: 'machine-cost-profile', component: MachineCostProfileComponent },
      { path: 'stock', component: GenStocksComponent },
      { path: 'store', component: GenStoreComponent },
      { path: 'purchase', component: PurchaseComponent },
      {
        path: 'issue-materials',
        loadComponent: () => import('./issue-materials/issue-materials.component').then(m => m.IssueMaterialsComponent)
      },
      {
        path: 'extra-material-request',
        loadComponent: () => import('./extra-material-request/extra-material-request.component').then(m => m.ExtraMaterialRequestComponent)
      },
      {
        path: 'purchase-requisition',
        loadComponent: () => import('./purchase-requisition/purchase-requisition.component').then(m => m.PurchaseRequisitionComponent)
      },
      {
        path: 'purchase-order',
        loadComponent: () => import('./purchase-order/purchase-order.component').then(m => m.PurchaseOrderComponent)
      },
      {
        path: 'reports',
        loadComponent: () => import('./reports/reports-landing.component').then(m => m.ReportsLandingComponent)
      },
      {
        path: 'reports/production-summary',
        loadComponent: () => import('./reports/production-report.component').then(m => m.ProductionReportComponent)
      },
      {
        path: 'reports/sales-dispatch',
        loadComponent: () => import('./reports/sales-report.component').then(m => m.SalesReportComponent)
      },
      {
        path: 'reports/inventory-valuation',
        loadComponent: () => import('./reports/inventory-report.component').then(m => m.InventoryReportComponent)
      },
      {
        path: 'reports/customer-tracking',
        loadComponent: () => import('./reports/customer-tracking.component').then(m => m.CustomerTrackingComponent)
      },
      {
        path: 'reports/bom-audit',
        loadComponent: () => import('./reports/bom-audit/bom-audit.component').then(m => m.BomAuditComponent)
      },
      {
        path: 'thread-stock',
        loadComponent: () => import('./thread-stock/thread-stock.component').then(m => m.ThreadStockComponent)
      },

      //Services & Rates
      { path: 'services', component: GenServicesComponent },
      { path: 'service-types', component: ServiceTypeComponent },
      { path: 'service-rates', component: GenServiceRatesComponent },
      /** ---------------- CHILD FALLBACK ---------------- */
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
    ]
  },
      /** ------------------------------------------------------
       *  FALLBACK
       * ------------------------------------------------------ */
      { path: '**', redirectTo: '' }    
];
  
