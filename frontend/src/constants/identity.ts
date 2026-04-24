
export const USER_TYPES = ['cliente', 'admin'] as const

export type UserType = (typeof USER_TYPES)[number]

export const USER_TYPE_OPTIONS: { label: string; value: UserType }[] = [
  { label: 'Cliente', value: 'cliente' },
  { label: 'Admin', value: 'admin' },
]
