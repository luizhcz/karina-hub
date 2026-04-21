import { ResponsiveContainer, PieChart, Pie, Cell, Tooltip, Legend } from 'recharts'

interface DonutChartProps {
  data: { name: string; value: number; color: string }[]
  height?: number
}

export function DonutChart({ data, height = 250 }: DonutChartProps) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <PieChart>
        <Pie data={data} dataKey="value" nameKey="name" cx="50%" cy="50%" innerRadius={50} outerRadius={80} paddingAngle={2}>
          {data.map((d, i) => (
            <Cell key={i} fill={d.color} />
          ))}
        </Pie>
        <Tooltip
          contentStyle={{ background: '#081529', border: '1px solid #1A3357', borderRadius: 8, fontSize: 12, color: '#DCE8F5' }}
        />
        <Legend wrapperStyle={{ fontSize: 12, color: '#B8CEE5' }} />
      </PieChart>
    </ResponsiveContainer>
  )
}
