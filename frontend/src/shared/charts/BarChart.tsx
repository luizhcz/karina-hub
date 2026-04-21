import { ResponsiveContainer, BarChart as ReBarChart, Bar, XAxis, YAxis, Tooltip, CartesianGrid } from 'recharts'

interface HorizontalBarChartProps {
  data: { name: string; value: number }[]
  color?: string
  height?: number
  valueFormatter?: (v: number) => string
}

export function HorizontalBarChart({ data, color = '#0057E0', height = 250, valueFormatter }: HorizontalBarChartProps) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <ReBarChart data={data} layout="vertical" margin={{ top: 5, right: 30, left: 80, bottom: 5 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#1A3357" horizontal={false} />
        <XAxis type="number" tick={{ fontSize: 11, fill: '#7596B8' }} tickLine={false} axisLine={false} tickFormatter={valueFormatter} />
        <YAxis type="category" dataKey="name" tick={{ fontSize: 11, fill: '#B8CEE5' }} tickLine={false} axisLine={false} width={70} />
        <Tooltip
          contentStyle={{ background: '#081529', border: '1px solid #1A3357', borderRadius: 8, fontSize: 12, color: '#DCE8F5' }}
          formatter={valueFormatter ? (v: number) => valueFormatter(v) : undefined}
        />
        <Bar dataKey="value" fill={color} radius={[0, 4, 4, 0]} />
      </ReBarChart>
    </ResponsiveContainer>
  )
}
