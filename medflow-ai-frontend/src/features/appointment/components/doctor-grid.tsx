import { DoctorCard } from './doctor-card';
import { DoctorEmptyState } from './doctor-empty-state';
import { DoctorSkeletonGrid } from './doctor-skeleton-grid';
import type { DoctorSearchResult } from '../types/doctor-search.types';

interface DoctorGridProps {
  doctors: DoctorSearchResult[];
  isLoading: boolean;
  onResetFilters: () => void;
  onViewProfile?: (doctor: DoctorSearchResult) => void;
}

export function DoctorGrid({ doctors, isLoading, onResetFilters, onViewProfile }: DoctorGridProps) {
  if (isLoading) {
    return <DoctorSkeletonGrid count={6} />;
  }

  if (doctors.length === 0) {
    return <DoctorEmptyState onResetFilters={onResetFilters} />;
  }

  return (
    <div className="grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-3">
      {doctors.map((doctor) => (
        <DoctorCard key={doctor.id} doctor={doctor} onViewProfile={onViewProfile} />
      ))}
    </div>
  );
}
