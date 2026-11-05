// src/app/components/models/components.dto.ts

// ================= LIST / TABLE =================
export interface ComponentRowViewModel {
  id: number;

  code: number;
  name: string;

  designId: number | null;
  designDisplay: string | null;

  headToHead: number;  
  stitch: number;
  tilla: number;
  seqA: number;
  seqB: number;

  branchId: number | null;
  branchName: string | null;

  active: boolean;
  deleted: boolean;
}


export interface ComponentDetailViewModel extends ComponentRowViewModel {

  // ================= AUDIT =================
  createdDate: Date | string | null;
  updatedDate: Date | string | null;

  createdByEmployeeId: number | null;
  updatedByEmployeeId: number | null;
}


// ================= CREATE / UPDATE =================
export interface ComponentCreateUpdateModel {
  id?: number;
  name: string;

  designId: number | null;  

  headToHead: number;
  stitch: number;
  tilla: number;
  seqA: number;
  seqB: number;

  branchId: number | null;
  active: boolean;
}



