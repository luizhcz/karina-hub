import { useState, useEffect } from 'react'
import { Input } from './Input'
import { Button } from './Button'
import { Badge } from './Badge'
import { useValidateSecret, type SecretValidateResponse } from '../../api/secrets'

interface SecretReferenceInputProps {
  label?: string
  value: string
  onChange: (value: string) => void
  placeholder?: string
  disabled?: boolean
  /** Quando true, mostra banner amarelo de "Legacy DPAPI credential — recadastrar". */
  legacyDpapi?: boolean
}

const AWS_PREFIX = 'secret://aws/'

export function SecretReferenceInput({
  label = 'AWS Secrets Manager reference',
  value,
  onChange,
  placeholder = 'secret://aws/efs-ai-hub/...',
  disabled,
  legacyDpapi,
}: SecretReferenceInputProps) {
  const [validation, setValidation] = useState<SecretValidateResponse | null>(null)
  const validate = useValidateSecret()

  // Reset validation quando o valor muda — força re-validate antes de submit
  useEffect(() => {
    setValidation(null)
  }, [value])

  const trimmed = value.trim()
  const hasInput = trimmed.length > 0
  const looksLikeAwsRef = trimmed.startsWith(AWS_PREFIX)
  const formatError =
    hasInput && !looksLikeAwsRef
      ? `Reference must start with "${AWS_PREFIX}".`
      : undefined

  async function handleValidate() {
    if (!hasInput || !looksLikeAwsRef) return
    try {
      const result = await validate.mutateAsync(trimmed)
      setValidation(result)
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Validation failed'
      setValidation({ exists: false, error: message })
    }
  }

  return (
    <div className="flex flex-col gap-2">
      {legacyDpapi && (
        <div className="rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-300">
          <strong>Legacy credential.</strong> Esta credencial está em formato DPAPI legacy.
          Recadastre apontando uma referência <code>{AWS_PREFIX}…</code> para migrar pro
          AWS Secrets Manager.
        </div>
      )}

      <div className="flex items-end gap-2">
        <div className="flex-1">
          <Input
            label={label}
            type="text"
            value={value}
            onChange={(e) => onChange(e.target.value)}
            placeholder={placeholder}
            disabled={disabled}
            error={formatError}
            autoComplete="off"
            spellCheck={false}
          />
        </div>
        <Button
          type="button"
          variant="secondary"
          size="md"
          loading={validate.isPending}
          onClick={handleValidate}
          disabled={disabled || !hasInput || !looksLikeAwsRef}
        >
          Validate
        </Button>
      </div>

      {validation && !validate.isPending && (
        <div className="flex items-center gap-2">
          {validation.exists ? (
            <>
              <Badge variant="green">Reference resolved</Badge>
              {validation.lastChanged && (
                <span className="text-xs text-text-muted">
                  last changed {new Date(validation.lastChanged).toLocaleString()}
                </span>
              )}
            </>
          ) : (
            <>
              <Badge variant="red">Not found</Badge>
              {validation.error && (
                <span className="text-xs text-red-400">{validation.error}</span>
              )}
            </>
          )}
        </div>
      )}
    </div>
  )
}
