import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule, NavigationEnd } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../shared/services/auth.service';

interface NavItem {
  label: string;
  route: string;
  icon?: string;
  /** Role names allowed to see this item. Empty = visible to all. */
  roles?: string[];
}

interface NavGroup {
  id: number;
  title: string;
  icon: string;
  accent?: 'default' | 'operations';
  items: NavItem[];
  /** When set, the whole group is hidden unless the user matches one of these roles. */
  roles?: string[];
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './sidebar.html',
  styleUrls: ['./sidebar.css']
})
export class SidebarComponent implements OnInit, OnDestroy {

  private routerSub!: Subscription;

  // ============================================================
  // Search / filter (live)
  // ============================================================
  search = '';

  // ============================================================
  // Menu definition — single source of truth.
  // ------------------------------------------------------------
  // Role gating is by ROLE NAME (case-insensitive). Empty roles[]
  // means "visible to everyone". The role name lookup falls back
  // gracefully — if the cached User has no RoleName (legacy login)
  // EVERYTHING is shown.
  // ============================================================
  readonly groups: NavGroup[] = [
    {
      id: 1, title: 'Dashboard', icon: 'bi-speedometer2',
      items: [
        { label: 'Overview', route: '/dashboard', icon: 'bi-grid' }
      ]
    },
    {
      id: 2, title: 'Administration', icon: 'bi-people',
      roles: ['Administrator', 'HR Officer'],
      items: [
        { label: 'Users', route: '/users', icon: 'bi-person-badge' },
        { label: 'Branches', route: '/branches', icon: 'bi-building' },
        { label: 'Companies', route: '/companies', icon: 'bi-buildings' },
        { label: 'Customers', route: '/customers', icon: 'bi-person-rolodex' },
        { label: 'Suppliers', route: '/suppliers', icon: 'bi-truck-front' }
      ]
    },
    {
      id: 3, title: 'Designs & Patterns', icon: 'bi-brush',
      roles: ['Administrator', 'Manager', 'Designer', 'Production Manager'],
      items: [
        { label: 'Designs', route: '/designs', icon: 'bi-palette' },
        { label: 'Design Costing', route: '/design-costing', icon: 'bi-cash-coin' },
        { label: 'Components', route: '/components', icon: 'bi-puzzle' },
        { label: 'Component Materials', route: '/component-materials', icon: 'bi-bricks' }
      ]
    },
    {
      id: 4, title: 'Operations', icon: 'bi-truck',
      accent: 'operations',
      items: [
        // Workflow order: Job Order → Production Plan → Work Order
        // → Work Order Items → Dispatch. Production Plan was previously
        // grouped under "Production" but it's the *scheduling* step
        // between sales (Job Order) and execution (Work Order), so it
        // belongs here in Operations between those two — not buried
        // among machine/QC/ledger items.
        { label: 'Job Orders', route: '/job-orders', icon: 'bi-clipboard-data',
          roles: ['Administrator', 'Manager', 'Salesman'] },
        { label: 'Production Plan', route: '/production-plan', icon: 'bi-calendar-week',
          roles: ['Administrator', 'Manager', 'Production Manager'] },
        { label: 'Work Orders', route: '/work-orders', icon: 'bi-card-checklist',
          roles: ['Administrator', 'Manager', 'Production Manager'] },
        { label: 'Work Order Items', route: '/work-order-items', icon: 'bi-list-task',
          roles: ['Administrator', 'Manager', 'Production Manager'] },
        { label: 'Dispatch', route: '/dispatch', icon: 'bi-box-arrow-right',
          roles: ['Administrator', 'Manager', 'Salesman', 'Cashier'] }
      ]
    },
    {
      id: 5, title: 'Production', icon: 'bi-hammer',
      roles: ['Administrator', 'Manager', 'Production Manager', 'Floor Supervisor', 'Quality Inspector', 'Machine Operator'],
      items: [
        // Production Plan moved up to Operations (see comment above).
        // This group now holds the *execution* side: what happened on
        // the floor today (ledger / daily / stops), QC, machine setup.
        { label: 'Production Ledger', route: '/production-ledger', icon: 'bi-journal-text' },
        { label: 'Daily Production', route: '/daily-production', icon: 'bi-bar-chart-line' },
        { label: 'Production Stops', route: '/daily-production-stops', icon: 'bi-stop-circle' },
        { label: 'QC Inspection', route: '/qc-inspection', icon: 'bi-clipboard-check',
          roles: ['Administrator', 'Manager', 'Production Manager', 'Quality Inspector'] },
        { label: 'Machines', route: '/machines', icon: 'bi-cpu',
          roles: ['Administrator', 'Manager', 'Production Manager'] },
        { label: 'Machine Parameters', route: '/machine-parameters', icon: 'bi-sliders',
          roles: ['Administrator', 'Production Manager'] },
        { label: 'Machine Cost Profile', route: '/machine-cost-profile', icon: 'bi-currency-rupee',
          roles: ['Administrator', 'Manager'] }
      ]
    },
    {
      id: 6, title: 'Products & Catalog', icon: 'bi-box',
      roles: ['Administrator', 'Manager', 'Production Manager', 'Store Keeper'],
      items: [
        { label: 'Products', route: '/products', icon: 'bi-box-seam' },
        { label: 'Threads', route: '/threads', icon: 'bi-bezier2' },
        { label: 'Bill of Materials (BOM)', route: '/bom', icon: 'bi-list-columns-reverse' },
        { label: 'Brands', route: '/brands', icon: 'bi-tag' },
        { label: 'Categories', route: '/categories', icon: 'bi-tags' }
      ]
    },
    {
      id: 9, title: 'Inventory & Purchasing', icon: 'bi-boxes',
      roles: ['Administrator', 'Manager', 'Store Keeper', 'Accountant', 'Cashier'],
      items: [
        { label: 'Stock', route: '/stock', icon: 'bi-bar-chart-steps' },
        { label: 'Store', route: '/store', icon: 'bi-shop' },
        { label: 'Purchase Requisition', route: '/purchase-requisition', icon: 'bi-clipboard-check' },
        { label: 'Purchase Order', route: '/purchase-order', icon: 'bi-file-earmark-text' },
        { label: 'Purchase (GRN)', route: '/purchase', icon: 'bi-bag-plus' },
        { label: 'Issue Materials (WO)', route: '/issue-materials', icon: 'bi-box-arrow-up-right' },
        { label: 'Extra Material Request', route: '/extra-material-request', icon: 'bi-clipboard-plus' }
      ]
    },
    {
      id: 7, title: 'Services & Rates', icon: 'bi-receipt-cutoff',
      roles: ['Administrator', 'Manager', 'Accountant'],
      items: [
        { label: 'The Services', route: '/services', icon: 'bi-card-text' },
        { label: 'Service Types', route: '/service-types', icon: 'bi-collection' },
        { label: 'Service Rates', route: '/service-rates', icon: 'bi-currency-dollar' }
      ]
    },
    {
      id: 10, title: 'Reports', icon: 'bi-bar-chart-line',
      roles: ['Administrator', 'Manager', 'Accountant', 'Production Manager'],
      items: [
        { label: 'All Reports', route: '/reports', icon: 'bi-grid-3x3-gap' },
        { label: 'Production Summary', route: '/reports/production-summary', icon: 'bi-gear-fill' },
        { label: 'Sales & Dispatch', route: '/reports/sales-dispatch', icon: 'bi-truck' },
        { label: 'Inventory Valuation', route: '/reports/inventory-valuation', icon: 'bi-boxes' },
        { label: 'Customer Tracking', route: '/reports/customer-tracking', icon: 'bi-card-checklist' },
        { label: 'BOM vs Actual', route: '/reports/bom-audit', icon: 'bi-clipboard-data' }
      ]
    },
    {
      id: 8, title: 'Settings', icon: 'bi-gear',
      roles: ['Administrator'],
      items: [
        { label: 'General', route: '/settings/general', icon: 'bi-sliders2' },
        { label: 'Profile', route: '/settings/profile', icon: 'bi-person-circle' }
      ]
    }
  ];

  groupOpen: Record<number, boolean> = {};

  /** Memoized — recomputed only when `search` or the user's role changes,
   *  NOT on every change-detection cycle. Returning a new array from a
   *  getter inside *ngFor causes Angular to spin. */
  visibleGroups: NavGroup[] = [];

  private userRole = '';

  constructor(private router: Router, private auth: AuthService) {
    // Default: only Dashboard expanded.
    this.groups.forEach(g => this.groupOpen[g.id] = g.id === 1);
    this.userRole = (this.auth.getUserRoleName() || '').toLowerCase();
    this.refreshVisibleGroups();
  }

  // ============================================================
  // Filtering — items visible after search + role gating.
  // ============================================================
  /** Returns true when the given roles[] permits the current user.
   *  Empty/undefined roles = open to everyone. If we don't know the
   *  user's role (legacy login), allow everything. */
  private isAllowed(roles: string[] | undefined): boolean {
    if (!roles || roles.length === 0) return true;
    if (!this.userRole) return true;
    return roles.some(r => r.toLowerCase() === this.userRole);
  }

  /** Recompute the menu projection. Called explicitly on inputs that
   *  affect filtering — never per change-detection tick. */
  private refreshVisibleGroups(): void {
    const q = this.search.trim().toLowerCase();

    this.visibleGroups = this.groups
      .filter(g => this.isAllowed(g.roles))
      .map(g => {
        const items = g.items
          .filter(i => this.isAllowed(i.roles))
          .filter(i => !q || i.label.toLowerCase().includes(q) || g.title.toLowerCase().includes(q));
        return { ...g, items };
      })
      .filter(g => g.items.length > 0);
  }

  onSearchChange(): void { this.refreshVisibleGroups(); }

  /** During a search, force every group to render expanded so matches are visible. */
  isOpen(groupId: number): boolean {
    if (this.search.trim().length > 0) return true;
    return !!this.groupOpen[groupId];
  }

  trackGroup = (_: number, g: NavGroup) => g.id;
  trackItem  = (_: number, i: NavItem)  => i.route;

  // ============================================================
  // Group toggle helpers
  // ============================================================
  toggleGroup(groupId: number): void {
    // Accordion behaviour preserved — opening one closes the others
    // (except Dashboard stays sticky).
    this.groups.forEach(g => {
      if (g.id === groupId) {
        this.groupOpen[g.id] = !this.groupOpen[g.id];
      } else {
        this.groupOpen[g.id] = false;
      }
    });
  }

  collapseAll(): void {
    this.groups.forEach(g => this.groupOpen[g.id] = false);
  }

  expandAll(): void {
    this.groups.forEach(g => this.groupOpen[g.id] = true);
  }

  clearSearch(): void {
    this.search = '';
    this.refreshVisibleGroups();
  }

  // ============================================================
  // Auto-open the group that owns the current route.
  // ============================================================
  ngOnInit(): void {
    this.syncGroupWithRoute(this.router.url);

    this.routerSub = this.router.events.subscribe(event => {
      if (event instanceof NavigationEnd) {
        this.syncGroupWithRoute(event.urlAfterRedirects);
      }
    });
  }

  ngOnDestroy(): void {
    this.routerSub?.unsubscribe();
  }

  private syncGroupWithRoute(url: string): void {
    // Don't fight a search — the user wants everything visible.
    if (this.search.trim().length > 0) return;

    // Find the group that contains the current route.
    const match = this.groups.find(g =>
      g.items.some(i => url.startsWith(i.route) || url.includes(i.route))
    );

    this.groups.forEach(g => this.groupOpen[g.id] = false);
    if (match) {
      this.groupOpen[match.id] = true;
    } else {
      // Fallback — keep dashboard open if nothing matched.
      this.groupOpen[1] = true;
    }
  }
}
