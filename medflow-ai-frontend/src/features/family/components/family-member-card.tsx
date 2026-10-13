import type { FamilyMember } from '../types/family.types';
import { DependentMemberCard } from './dependent-member-card';
import { PrimaryMemberCard } from './primary-member-card';

interface FamilyMemberCardProps {
  member: FamilyMember;
  onBookAppointment?: (member: FamilyMember) => void;
  onEditMember?: (member: FamilyMember) => void;
}

export function FamilyMemberCard({
  member,
  onBookAppointment,
  onEditMember,
}: FamilyMemberCardProps) {
  if (member.isPrimaryAccount) {
    return <PrimaryMemberCard member={member} onBookAppointment={onBookAppointment} />;
  }

  return (
    <DependentMemberCard
      member={member}
      onBookAppointment={onBookAppointment}
      onEditMember={onEditMember}
    />
  );
}
