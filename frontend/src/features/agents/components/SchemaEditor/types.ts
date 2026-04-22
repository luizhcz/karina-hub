export type SchemaType = 'string' | 'number' | 'integer' | 'boolean' | 'object' | 'array'

export const SCHEMA_TYPES: SchemaType[] = ['string', 'number', 'integer', 'boolean', 'object', 'array']

/**
 * Representação mínima de um node JSON Schema manipulado pelo editor.
 * JSON Schema é estruturalmente recursivo e extensível — usamos type local
 * ao invés de importar uma lib inteira (`json-schema`). Campos comuns ficam
 * tipados explicitamente; extensões arbitrárias caem em `[key: string]: unknown`.
 */
export interface JsonSchemaNode {
  type?: SchemaType | SchemaType[] | 'null' | ('null' | SchemaType)[]
  description?: string
  anyOf?: JsonSchemaNode[]
  oneOf?: JsonSchemaNode[]
  allOf?: JsonSchemaNode[]
  properties?: Record<string, JsonSchemaNode>
  required?: string[]
  items?: JsonSchemaNode
  enum?: unknown[]
  default?: unknown
  [key: string]: unknown
}

export interface ResolvedType {
  types: SchemaType[]
  nullable: boolean
  resolved: JsonSchemaNode
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
