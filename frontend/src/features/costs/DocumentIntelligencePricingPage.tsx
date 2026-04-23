import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import {
  useDocumentIntelligencePricings,
  useCreateDocumentIntelligencePricing,
  useDeleteDocumentIntelligencePricing,
} from '../../api/documentIntelligence'
import type {
  DocumentIntelligencePricing,
  CreateDocumentIntelligencePricingRequest,
} from '../../api/documentIntelligence'
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
  pricePerPage: string
  currency: string
  effectiveFrom: string
}

const defaultForm: PricingFormState = {
  modelId: 'prebuilt-layout',
  provider: 'AZUREAI',
  pricePerPage: '0.01',
  currency: 'USD',
  effectiveFrom: new Date().toISOString().slice(0, 10),
}

export function DocumentIntelligencePricingPage() {
  const { data: pricings, isLoading, error, refetch } = useDocumentIntelligencePricings()
  const createPricing = useCreateDocumentIntelligencePricing()
  const deletePricing = useDeleteDocumentIntelligencePricing()

  const [showCreate, setShowCreate] = useState(false)
  const [deletingId, setDeletingId] = useState<number | null>(null)
  const [form, setForm] = useState<PricingFormState>(defaultForm)
  const [formError, setFormError] = useState<string | null>(null)

  if (isLoading) return <PageLoader />
  if (error) {
    return <ErrorCard message="Erro ao carregar pricing do Document Intelligence" onRetry={refetch} />
  }

  const items = pricings ?? []

  const handleCreate = async () => {
    if (!form.modelId.trim()) {
      setFormError('Model ID é obrigatório')
      return
    }
    if (!form.provider.trim()) {
      setFormError('Provider é obrigatório')
      return
    }

    const body: CreateDocumentIntelligencePricingRequest = {
      modelId: form.modelId,
      provider: form.provider,
      pricePerPage: Number(form.pricePerPage),
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

  const columns: ColumnDef<DocumentIntelligencePricing, unknown>[] = [
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
      cell: ({ getValue }) => <span className="text-text-secondary">{String(getValue())}</span>,
    },
    {
      accessorKey: 'pricePerPage',
      header: 'Preço / página',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs">${Number(getValue()).toFixed(4)}</span>
      ),
    },
    {
      id: 'per1000',
      header: '/ 1.000 páginas',
      cell: ({ row }) => (
        <span className="font-mono text-xs text-text-muted">
          ${(Number(row.original.pricePerPage) * 1000).toFixed(2)}
        </span>
      ),
    },
    { accessorKey: 'currency', header: 'Moeda' },
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
          <h1 className="text-2xl font-bold text-text-primary">Document Intelligence — Pricing</h1>
          <p className="text-sm text-text-muted mt-1">
            Preços por página. Atualização de valor = nova linha com nova vigência.
          </p>
        </div>
        <Button onClick={() => setShowCreate(true)}>Adicionar Pricing</Button>
      </div>

      <Card padding={false}>
        {items.length === 0 ? (
          <EmptyState
            title="Nenhum pricing configurado"
            description="Rode db/seed_document_intelligence_pricing.sql ou adicione manualmente."
            action={<Button onClick={() => setShowCreate(true)}>Adicionar Pricing</Button>}
          />
        ) : (
          <DataTable data={items} columns={columns} searchPlaceholder="Buscar modelo..." />
        )}
      </Card>

      <Modal
        open={showCreate}
        onClose={() => setShowCreate(false)}
        title="Adicionar Pricing (Document Intelligence)"
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
            placeholder="prebuilt-layout"
          />
          <Input
            label="Provider *"
            value={form.provider}
            onChange={(e) => setForm({ ...form, provider: e.target.value })}
            placeholder="AZUREAI"
          />
          <Input
            label="Preço / página (USD)"
            type="number"
            step="0.0001"
            value={form.pricePerPage}
            onChange={(e) => setForm({ ...form, pricePerPage: e.target.value })}
          />
          <p className="text-xs text-text-muted -mt-2">
            Referência: Azure publica em USD / 1.000 páginas — divida por 1000. Ex: Layout $10/1k = $0.01/pág.
          </p>
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
            <Button variant="ghost" onClick={() => setShowCreate(false)}>
              Cancelar
            </Button>
            <Button onClick={handleCreate} loading={createPricing.isPending}>
              Salvar
            </Button>
          </div>
        </div>
      </Modal>

      <ConfirmDialog
        open={deletingId !== null}
        title="Excluir pricing?"
        message="Essa ação é irreversível. O preço será removido do banco."
        confirmLabel="Excluir"
        variant="danger"
        onClose={() => setDeletingId(null)}
        onConfirm={async () => {
          if (deletingId) await deletePricing.mutateAsync(deletingId)
          setDeletingId(null)
        }}
      />
    </div>
  )
}
