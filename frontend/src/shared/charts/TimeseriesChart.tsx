import { ResponsiveContainer, BarChart, Bar, XAxis, YAxis, Tooltip, CartesianGrid, Legend } from 'recharts'

interface Series {
  key: string
  label: string
  color: string
}

interface TimeseriesChartProps {
  data: Record<string, unknown>[]
  xKey: string
  series: Series[]
  height?: number
  stacked?: boolean
}

export function TimeseriesChart({ data, xKey, series, height = 300, stacked = false }: TimeseriesChartProps) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={data} margin={{ top: 5, right: 10, left: 0, bottom: 5 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#1A3357" vertical={false} />
        <XAxis dataKey={xKey} tick={{ fontSize: 11, fill: '#7596B8' }} tickLine={false} axisLine={{ stroke: '#1A3357' }} />
        <YAxis tick={{ fontSize: 11, fill: '#7596B8' }} tickLine={false} axisLine={false} />
        <Tooltip
          contentStyle={{ background: '#081529', border: '1px solid #1A3357', borderRadius: 8, fontSize: 12, color: '#DCE8F5' }}
        />
        <Legend wrapperStyle={{ fontSize: 12, color: '#B8CEE5' }} />
        {series.map((s) => (
          <Bar key={s.key} dataKey={s.key} name={s.label} fill={s.color} stackId={stacked ? 'stack' : undefined} radius={[3, 3, 0, 0]} />
        ))}
      </BarChart>
    </ResponsiveContainer>
  )
}
