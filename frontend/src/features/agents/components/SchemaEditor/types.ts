export type SchemaType = 'string' | 'number' | 'integer' | 'boolean' | 'object' | 'array'

export const SCHEMA_TYPES: SchemaType[] = ['string', 'number', 'integer', 'boolean', 'object', 'array']

export interface ResolvedType {
  types: SchemaType[]
  nullable: boolean
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  resolved: Record<string, any>
}

export interface FieldEditData {
  key: string
  oldKey: string
  type: SchemaType
  description: string
  nullable: boolean
  required: boolean
  isNew?: boolean
}

export interface SchemaEditorProps {
  value: string
  onChange: (v: string) => void
}

export type EditorMode = 'visual' | 'json'
