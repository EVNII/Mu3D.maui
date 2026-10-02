// Nearest-rank percentiles; zero samples remain unavailable, never fabricated zeros.
export function summarize(values) {
  if (!values.length) return null;
  if (values.some(value => !Number.isFinite(value) || value < 0)) throw new TypeError('Invalid benchmark sample.');
  const sorted = [...values].sort((a,b) => a-b);
  return {count: sorted.length, mean: values.reduce((a,b) => a+b,0) / values.length,
    median: sorted[Math.ceil(sorted.length * 0.5)-1], p95: sorted[Math.ceil(sorted.length * 0.95)-1],
    min: sorted[0], max: sorted.at(-1)};
}
