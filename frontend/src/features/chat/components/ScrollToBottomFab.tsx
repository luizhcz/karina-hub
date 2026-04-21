import { cn } from '../../../shared/utils/cn'

interface ScrollToBottomFabProps {
  visible: boolean
  unreadCount: number
  onClick: () => void
}

export function ScrollToBottomFab({ visible, unreadCount, onClick }: ScrollToBottomFabProps) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'absolute bottom-20 right-6 w-10 h-10 rounded-full bg-bg-tertiary border border-border-secondary',
        'flex items-center justify-center text-text-muted hover:text-text-primary hover:bg-bg-secondary',
        'shadow-lg transition-all duration-200',
        visible ? 'opacity-100 translate-y-0' : 'opacity-0 translate-y-4 pointer-events-none',
      )}
      title="Ir para o final"
    >
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-5 h-5">
        <path fillRule="evenodd" d="M10 3a.75.75 0 01.75.75v10.638l3.96-4.158a.75.75 0 111.08 1.04l-5.25 5.5a.75.75 0 01-1.08 0l-5.25-5.5a.75.75 0 111.08-1.04l3.96 4.158V3.75A.75.75 0 0110 3z" clipRule="evenodd" />
      </svg>
      {unreadCount > 0 && (
        <span className="absolute -top-1.5 -right-1.5 min-w-[18px] h-[18px] rounded-full bg-accent-blue text-white text-[10px] font-medium flex items-center justify-center px-1">
          {unreadCount > 99 ? '99+' : unreadCount}
        </span>
      )}
    </button>
  )
}
