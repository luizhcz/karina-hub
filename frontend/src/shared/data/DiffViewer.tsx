import ReactDiffViewer from 'react-diff-viewer-continued'

interface DiffViewerProps {
  oldValue: string
  newValue: string
  oldTitle?: string
  newTitle?: string
  splitView?: boolean
}

export function DiffViewer({ oldValue, newValue, oldTitle, newTitle, splitView = true }: DiffViewerProps) {
  return (
    <div className="rounded-lg overflow-hidden border border-border-primary">
      <ReactDiffViewer
        oldValue={oldValue}
        newValue={newValue}
        leftTitle={oldTitle}
        rightTitle={newTitle}
        splitView={splitView}
        useDarkTheme
        styles={{
          variables: {
            dark: {
              diffViewerBackground: '#081529',
              addedBackground: '#10b98120',
              removedBackground: '#ef444420',
              wordAddedBackground: '#10b98140',
              wordRemovedBackground: '#ef444440',
              addedGutterBackground: '#10b98115',
              removedGutterBackground: '#ef444415',
              gutterBackground: '#0C1D38',
              gutterBackgroundDark: '#04091A',
              codeFoldBackground: '#0C1D38',
              codeFoldGutterBackground: '#0C1D38',
              codeFoldContentColor: '#7596B8',
              emptyLineBackground: '#081529',
            },
          },
        }}
      />
    </div>
  )
}
