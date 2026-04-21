import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useModelPricings, useCreateModelPricing, useDeleteModelPricing } from '../../api/pricing'
import type { ModelPricing, CreateModelPricingRequest } from '../../api/pricing'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Button } from '../../shared/ui/Button'
import { Input } from '../../shared/ui/Input'
import { Modal } from '../../shared/ui/Modal'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'

interface PricingFormState {
  modelId: string
  provider: string
  pricePerInputToken: string
  pricePerOutputToken: string
  currency: string
  effectiveFrom: string
}

const defaultForm: PricingFormState = {
  modelId: '',
  provider: '',
  pricePerInputToken: '0.000001',
  pricePerOutputToken: '0.000003',
  currency: 'USD',
  effectiveFrom: new Date().toISOString().slice(0, 10),
}

export function ModelPricingPage() {
  const { data: pricings, isLoading, error, refetch } = useModelPricings()
  const createPricing = useCreateModelPricing()
  const deletePricing = useDeleteModelPricing()

  const [showCreate, setShowCreate] = useState(false)
  const [deletingId, setDeletingId] = useState<number | null>(null)
  const [form, setForm] = useState<PricingFormState>(defaultForm)
  const [formError, setFormError] = useState<string | null>(null)

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar pricing" onRetry={refetch} />

  const items = pricings ?? []

  const handleCreate = async () => {
    if (!form.modelId.trim()) { setFormError('Model ID é obrigatório'); return }
    if (!form.provider.trim()) { setFormError('Provider é obrigatório'); return }

    const body: CreateModelPricingRequest = {
      modelId: form.modelId,
      provider: form.provider,
      pricePerInputToken: Number(form.pricePerInputToken),
      pricePerOutputToken: Number(form.pricePerOutputToken),
      currency: form.currency || 'USD',
      effectiveFrom: form.effectiveFrom || new Date().toISOString(),
    }

    try {
      await createPricing.mutateAsync(body)
      setShowCreate(false)
      setForm(defaultForm)
      setFormError(null)
    } catch {
      setFormError('Erro ao criar pricing.')
    }
  }

  const columns: ColumnDef<ModelPricing, unknown>[] = [
    {
      accessorKey: 'modelId',
      header: 'Model ID',
      cell: ({ getValue }) => (
        <span className="font-mono text-sm text-text-primary">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'provider',
      header: 'Provider',
      cell: ({ getValue }) => (
        <span className="text-text-secondary">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'pricePerInputToken',
      header: 'Input / token',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs">${Number(getValue()).toFixed(8)}</span>
      ),
    },
    {
      accessorKey: 'pricePerOutputToken',
      header: 'Output / token',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs">${Number(getValue()).toFixed(8)}</span>
      ),
    },
    {
      accessorKey: 'currency',
      header: 'Moeda',
    },
    {
      accessorKey: 'effectiveFrom',
      header: 'Vigência',
      cell: ({ getValue }) => (
        <span className="text-xs text-text-muted">
          {new Date(String(getValue())).toLocaleDateString('pt-BR')}
        </span>
      ),
    },
    {
      id: 'actions',
      header: 'Ações',
      cell: ({ row }) => (
        <Button
          variant="danger"
          size="sm"
          onClick={(e) => {
            e.stopPropagation()
            setDeletingId(row.original.id)
          }}
        >
          Excluir
        </Button>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Model Pricing</h1>
          <p className="text-sm text-text-muted mt-1">Gerenciar preços por modelo e provider</p>
        </div>
        <Button onClick={() => setShowCreate(true)}>Adicionar Pricing</Button>
      </div>

      <Card padding={false}>
        {items.length === 0 ? (
          <EmptyState
            title="Nenhum pricing configurado"
            description="Adicione preços para calcular custos de token usage."
            action={<Button onClick={() => setShowCreate(true)}>Adicionar Pricing</Button>}
          />
        ) : (
          <DataTable
            data={items}
            columns={columns}
            searchPlaceholder="Buscar modelo..."
          />
        )}
      </Card>

      <Modal
        open={showCreate}
        onClose={() => setShowCreate(false)}
        title="Adicionar Pricing"
        size="md"
      >
        <div className="flex flex-col gap-4">
          {formError && (
            <div className="bg-red-500/10 border border-red-500/30 rounded-lg p-3 text-sm text-red-400">
              {formError}
            </div>
          )}
          <Input
            label="Model ID *"
            value={form.modelId}
            onChange={(e) => setForm({ ...form, modelId: e.target.value })}
            placeholder="gpt-4o"
          />
          <Input
            label="Provider *"
            value={form.provider}
            onChange={(e) => setForm({ ...form, provider: e.target.value })}
            placeholder="OpenAI"
          />
          <div className="grid grid-cols-2 gap-3">
            <Input
              label="Preço / Input Token"
              type="number"
              step="0.000000001"
              value={form.pricePerInputToken}
              onChange={(e) => setForm({ ...form, pricePerInputToken: e.target.value })}
            />
            <Input
              label="Preço / Output Token"
              type="number"
              step="0.000000001"
              value={form.pricePerOutputToken}
              onChange={(e) => setForm({ ...form, pricePerOutputToken: e.target.value })}
            />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <Input
              label="Moeda"
              value={form.currency}
              onChange={(e) => setForm({ ...form, currency: e.target.value })}
              placeholder="USD"
            />
            <Input
              label="Vigência a partir de"
              type="date"
              value={form.effectiveFrom}
              onChange={(e) => setForm({ ...form, effectiveFrom: e.target.value })}
            />
          </div>
          <div className="flex justify-end gap-3 pt-2">
            <Button variant="ghost" onClick={() => setShowCreate(false)}>Cancelar</Button>
            <Button onClick={handleCreate} loading={createPricing.isPending}>Salvar</Button>
          </div>
        </div>
      </Modal>

      <ConfirmDialog
        open={deletingId !== null}
        onClose={() => setDeletingId(null)}
        onConfirm={() => {
          if (deletingId !== null) {
            deletePricing.mutate(deletingId, { onSuccess: () => setDeletingId(null) })
          }
        }}
        title="Excluir Pricing"
        message="Tem certeza que deseja excluir este pricing? Esta ação não pode ser desfeita."
        confirmLabel="Excluir"
        variant="danger"
        loading={deletePricing.isPending}
      />
    </div>
  )
}
