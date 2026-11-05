import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { AuthService } from '../shared/services/auth.service';

type RangeKey = 'today' | 'yesterday' | 'week' | 'month' | 'custom';

interface DashboardAlert {
  level: 'warn' | 'info' | 'danger';
  icon: string;
  label: string;
  value: number;
  route: string;
}

interface WorkflowStage {
  key: string;
  icon: string;
  label: string;
  hint: string;
  count: number;
  route: string;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit {
  userName = '';
  userImage = '';
  userImageError = false;

  loading = false;

  // ============================================================
  // Date range selector — UI only for Phase 1. The backend will
  // gain fromDate/toDate parameters in a follow-up; for now the
  // selected range is shown on screen but the data returned is
  // the live snapshot from /auth/dashboard.
  // ============================================================
  ranges: { key: RangeKey; label: string }[] = [
    { key: 'today',     label: 'Today' },
    { key: 'yesterday', label: 'Yesterday' },
    { key: 'week',      label: 'This Week' },
    { key: 'month',     label: 'This Month' }
  ];
  selectedRange: RangeKey = 'today';
  rangeLabel = 'Today';

  data: any = {
    productionSummary: {},
    qcSummary: {},
    stockSummary: {},
    dispatchSummary: {},
    machineSummary: {},
    operatorSummary: {}
  };

  /** Memoized alerts. Recomputed only after data loads — NEVER as a
   *  getter, since *ngFor over a getter that returns a new array
   *  re-triggers change detection in a loop. */
  alerts: DashboardAlert[] = [];

  /** Memoized workflow pipeline. Same change-detection rule as alerts. */
  workflow: WorkflowStage[] = [];
  workflowTotalPending = 0;

  constructor(public auth: AuthService, private router: Router) { }

  ngOnInit(): void {
    const user = this.auth.getUser();

    if (user) {
      this.userName = user.Name || user.UserName || 'User';
      this.userImage = this.auth.getUserImage();
    }

    this.loadDashboard();
  }

  loadDashboard(): void {
    this.loading = true;

    this.auth.getDashboard().subscribe({
      next: (res: any) => {
        this.data = res?.data ?? res?.Data ?? {};
        this.loading = false;
        this.recomputeAlerts();
        this.recomputeWorkflow();
      },
      error: () => {
        this.loading = false;
        this.recomputeAlerts();
        this.recomputeWorkflow();
      }
    });
  }

  onRangeChange(key: RangeKey): void {
    this.selectedRange = key;
    this.rangeLabel = this.ranges.find(r => r.key === key)?.label ?? 'Today';
    // Backend not yet date-aware. Reload anyway so a refresh after
    // selecting a range still pulls fresh data.
    this.loadDashboard();
  }

  n(value: any): number {
    return Number(value ?? 0);
  }

  /** Build the alerts strip from the freshly-loaded payload.
   *  Each alert is click-through. */
  private recomputeAlerts(): void {
    const out: DashboardAlert[] = [];

    const pendingQc = this.n(this.data?.qcSummary?.pendingQcQty);
    if (pendingQc > 0) {
      out.push({
        level: 'warn',
        icon: 'bi-shield-check',
        label: 'Pending QC',
        value: pendingQc,
        route: '/qc-inspection'
      });
    }

    const rework = this.n(this.data?.qcSummary?.reworkQty) || this.n(this.data?.stockSummary?.reworkQty);
    if (rework > 0) {
      out.push({
        level: 'warn',
        icon: 'bi-arrow-clockwise',
        label: 'Awaiting Rework',
        value: rework,
        route: '/qc-inspection'
      });
    }

    const reversed = this.n(this.data?.qcSummary?.reversedQcCount) || this.n(this.data?.stockSummary?.reversedQcCount);
    if (reversed > 0) {
      out.push({
        level: 'danger',
        icon: 'bi-exclamation-triangle',
        label: 'Reversed QC',
        value: reversed,
        route: '/qc-inspection'
      });
    }

    this.alerts = out;
  }

  trackAlert = (_: number, a: DashboardAlert) => a.label;

  /** Build the Workflow Pipeline widget from the workflowSummary payload.
   *  Order matches the physical production flow so the eye scans WO →
   *  Material Issue → Production → QC → Dispatch in order. */
  private recomputeWorkflow(): void {
    const w = this.data?.workflowSummary ?? {};

    this.workflowTotalPending = this.n(w.totalPending);

    this.workflow = [
      {
        key: 'wo-material',
        icon: 'bi-box-arrow-up-right',
        label: 'Issue Materials',
        hint: 'WOs awaiting material issue',
        count: this.n(w.wosAwaitingMaterialIssue),
        route: '/issue-materials'
      },
      {
        key: 'dp-draft',
        icon: 'bi-pencil-square',
        label: 'Daily Production',
        hint: 'Drafts to post to WIP',
        count: this.n(w.dpDrafts),
        route: '/daily-production'
      },
      {
        key: 'dp-qc-send',
        icon: 'bi-send',
        label: 'Send to QC',
        hint: 'Posted production awaiting QC',
        count: this.n(w.dpAwaitingQcSend),
        route: '/daily-production'
      },
      {
        key: 'qc-inspect',
        icon: 'bi-clipboard-check',
        label: 'QC Inspection',
        hint: 'Pending inspection',
        count: this.n(w.qcPendingInspection),
        route: '/qc-inspection'
      },
      {
        key: 'qc-rework',
        icon: 'bi-tools',
        label: 'Awaiting Rework',
        hint: 'Operators need to re-do',
        count: this.n(w.qcAwaitingRework),
        route: '/daily-production'
      },
      {
        key: 'qc-reinspect',
        icon: 'bi-arrow-clockwise',
        label: 'Re-inspection',
        hint: 'Rework done, awaiting QC',
        count: this.n(w.qcAwaitingReinspection),
        route: '/qc-inspection'
      },
      {
        key: 'dispatch-draft',
        icon: 'bi-truck',
        label: 'Dispatch Drafts',
        hint: 'Ready to post',
        count: this.n(w.dispatchDrafts),
        route: '/dispatch'
      }
    ];
  }

  trackWorkflow = (_: number, s: WorkflowStage) => s.key;

  /** Click-through helpers — KPI tile navigates to the relevant module. */
  goTo(route: string): void {
    if (route) this.router.navigate([route]);
  }

  onImageError(): void {
    this.userImageError = true;
  }

  getUserInitial(): string {
    return this.userName?.charAt(0)?.toUpperCase() || '?';
  }

  logout(): void {
    this.auth.logout().subscribe(() => {
      this.router.navigate(['/home']);
    });
  }
}
