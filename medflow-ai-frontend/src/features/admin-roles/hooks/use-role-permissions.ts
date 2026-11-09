import {
  QueryClient,
  QueryClientContext,
  useMutation,
  useQuery,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query';
import { useContext } from 'react';

import {
  assignRolePermissionsApi,
  createRoleApi,
  fetchRolesPermissionsCatalogApi,
} from '../api/role-permissions.api';
import { getMockRolesCatalog } from '../data/role-mock-data';
import type {
  CreateRoleRequest,
  RoleItem,
  RolesPermissionsCatalogDto,
  UpdateRolePermissionsRequest,
} from '../types/role.types';

export const ROLE_PERMISSIONS_QUERY_KEY = ['admin', 'roles-permissions'] as const;

const fallbackQueryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: false },
    mutations: { retry: false },
  },
});

export function useRolesPermissionsCatalog(): UseQueryResult<RolesPermissionsCatalogDto, Error> {
  const contextQueryClient = useContext(QueryClientContext);
  const queryClient = contextQueryClient ?? fallbackQueryClient;

  return useQuery(
    {
      queryKey: ROLE_PERMISSIONS_QUERY_KEY,
      queryFn: () => fetchRolesPermissionsCatalogApi({ page: 1, pageSize: 100 }),
      placeholderData: () => getMockRolesCatalog(),
      retry: false,
    },
    queryClient,
  );
}

export function useAssignRolePermissions(): UseMutationResult<
  RoleItem,
  Error,
  UpdateRolePermissionsRequest
> {
  const contextQueryClient = useContext(QueryClientContext);
  const queryClient = contextQueryClient ?? fallbackQueryClient;

  return useMutation(
    {
      mutationFn: (payload) => assignRolePermissionsApi(payload),
      onSuccess: (updatedRole) => {
        queryClient.setQueryData<RolesPermissionsCatalogDto>(ROLE_PERMISSIONS_QUERY_KEY, (prev) => {
          const base = prev ?? getMockRolesCatalog();
          const roles = base.roles.map((role) =>
            role.id === updatedRole.id || role.code === updatedRole.code ? updatedRole : role,
          );
          return {
            ...base,
            roles,
          };
        });
      },
    },
    queryClient,
  );
}

export function useCreateRole(): UseMutationResult<RoleItem, Error, CreateRoleRequest> {
  const contextQueryClient = useContext(QueryClientContext);
  const queryClient = contextQueryClient ?? fallbackQueryClient;

  return useMutation(
    {
      mutationFn: (payload) => createRoleApi(payload),
      onSuccess: (createdRole) => {
        queryClient.setQueryData<RolesPermissionsCatalogDto>(ROLE_PERMISSIONS_QUERY_KEY, (prev) => {
          const base = prev ?? getMockRolesCatalog();
          const exists = base.roles.some(
            (role) => role.id === createdRole.id || role.code === createdRole.code,
          );
          const roles = exists
            ? base.roles.map((role) =>
                role.id === createdRole.id || role.code === createdRole.code ? createdRole : role,
              )
            : [...base.roles, createdRole];

          return {
            ...base,
            roles,
            metrics: {
              ...base.metrics,
              totalRoles: roles.length,
              activeRoles: roles.filter((role) => role.status === 'ACTIVE').length,
              inactiveRoles: roles.filter((role) => role.status !== 'ACTIVE').length,
              systemRoles: roles.filter((role) => role.isSystemRole).length,
              customRoles: roles.filter((role) => !role.isSystemRole).length,
              totalAssignedAccounts: roles.reduce((sum, role) => sum + role.assignedUsersCount, 0),
              totalPermissions: base.permissions.length,
            },
          };
        });
      },
    },
    queryClient,
  );
}
