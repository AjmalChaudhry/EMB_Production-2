export interface RoleRowViewModel {
  Id: number;
  Name: string;
  Description?: string;
  Authority?: string; // e.g., permissions/working criteria
  Active: boolean;
  Deleted: boolean;

  // Audit
  CreatedBy?: number;
  CreatedByName?: string;
  CreatedDate?: Date;
  UpdatedBy?: number;
  UpdatedByName?: string;
  UpdatedDate?: Date;
}

export interface RolesPageViewModel {
  Search?: string;
  ActiveFilter?: string;

  Roles: RoleRowViewModel[];

  // Create/Edit toggles
  ShowCreate?: boolean;
  ShowEdit?: boolean;
  EditId?: number;

  // Payloads
  NewRole?: RoleRowViewModel;
  EditRole?: RoleRowViewModel;
}
