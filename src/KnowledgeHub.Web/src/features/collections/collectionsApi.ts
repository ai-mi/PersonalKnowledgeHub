import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '../../api/client'

export interface Collection {
  id: string
  name: string
  description: string | null
  itemCount: number
}

export interface CollectionInput {
  name: string
  description: string | null
}

const queryKey = ['collections'] as const

export const useCollections = () =>
  useQuery({ queryKey, queryFn: () => apiFetch<Collection[]>('/api/collections') })

export function useCollectionMutations() {
  const queryClient = useQueryClient()
  const onSuccess = () => queryClient.invalidateQueries({ queryKey })
  return {
    create: useMutation({
      mutationFn: (input: CollectionInput) => apiFetch<Collection>('/api/collections', { method: 'POST', body: input }),
      onSuccess,
    }),
    update: useMutation({
      mutationFn: ({ id, input }: { id: string; input: CollectionInput }) =>
        apiFetch<Collection>(`/api/collections/${id}`, { method: 'PUT', body: input }),
      onSuccess,
    }),
    remove: useMutation({
      mutationFn: (id: string) => apiFetch(`/api/collections/${id}`, { method: 'DELETE' }),
      onSuccess,
    }),
  }
}
