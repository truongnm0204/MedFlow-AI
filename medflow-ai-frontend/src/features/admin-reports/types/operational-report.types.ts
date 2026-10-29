export type AggregationStatus = 'AGGREGATED' | 'NEEDS_REFRESH';

export type OperationalReportStatus = 'COMPLETED' | 'NEEDS_REFRESH' | 'NEEDS_REVIEW';

export interface OperationalReportItem {
  id: string;
  code: string;
  name: string;
  periodLabel: string;
  category: string;
  facility: string;
  intakeVolumeLabel: string;
  medianWaitLabel: string;
  aggregationStatus: AggregationStatus;
  status: OperationalReportStatus;
}

export interface HourlyIntakePoint {
  hour: string;
  count: number;
  isPeak?: boolean;
}

export interface DepartmentVisitItem {
  department: string;
  count: number;
}

export interface VisitStatusSegment {
  key: 'COMPLETED' | 'IN_PROGRESS' | 'WAITING';
  label: string;
  countLabel: string;
  percentLabel: string;
  percentValue: number;
  barColorClass: string;
  swatchColorClass: string;
}
