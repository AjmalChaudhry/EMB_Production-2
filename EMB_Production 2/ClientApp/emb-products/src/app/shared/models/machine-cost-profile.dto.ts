export interface MachineCostProfileDto {

  Id: number;
  MachineId: number;
  MachineCode: string;

  HourlyRate?: number;
  PowerKW?: number;
  StitchPerMinute?: number;

  OperatorHourlyRate?: number;
  ElectricityUnitRate?: number;

  EffectiveFromText?: string;

  Active?: boolean;

}

export interface MachineCostProfileDetail {
  id: number;
  machineId: number;
  machineCode: string;

  hourlyRate: number;
  powerKW: number;
  stitchPerMinute: number;

  operatorHourlyRate: number;
  electricityUnitRate: number;

  effectiveFrom: string;
  effectiveFromText: string;

  active: boolean;

  createdDateText?: string;
  updatedDateText?: string;
}
