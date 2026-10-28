import type { ExaminationService } from '../types/service-search.types';
import { ServiceCard } from './service-card';
import { ServiceEmptyState } from './service-empty-state';
import { ServiceSkeletonGrid } from './service-skeleton-grid';

interface ServiceGridProps {
  isLoading: boolean;
  services: ExaminationService[];
  onViewDetail: (service: ExaminationService) => void;
  onSelectService: (service: ExaminationService) => void;
  onResetFilters: () => void;
}

export function ServiceGrid({
  isLoading,
  services,
  onViewDetail,
  onSelectService,
  onResetFilters,
}: ServiceGridProps) {
  if (isLoading) {
    return <ServiceSkeletonGrid />;
  }

  if (services.length === 0) {
    return <ServiceEmptyState onReset={onResetFilters} />;
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
      {services.map((service) => (
        <ServiceCard
          key={service.id}
          service={service}
          onViewDetail={onViewDetail}
          onSelectService={onSelectService}
        />
      ))}
    </div>
  );
}

