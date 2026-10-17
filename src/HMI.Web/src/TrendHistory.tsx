import { useCallback, useEffect, useMemo, useState } from "react";
import { ArrowLeft, Download, RefreshCw, TrendingUp } from "lucide-react";

const API = (import.meta.env.VITE_API_BASE as string | undefined) ?? "";

type TrendVariable = {
  varId: string;
  name: string;
  dataType: string;
  cycleMs: number;
  quality: string;
};
type TrendSample = {
  sequence: number;
  varId: string;
  value: string | null;
  quality: string;
  sampledAtUtc: string;
};
type TrendResponse = {
  varId: string;
  count: number;
  limit: number;
  samples: TrendSample[];
};

function csvCell(value: string | null) {
  let safe = value ?? "";
  if (/^[=+@\\-\\t\\r]/.test(safe)) safe = `\u0027${safe}`;
  return `"${safe.replace(/"/g, "\"\"")}"`;
}

function exportSamples(varId: string, samples: TrendSample[]) {
  const rows = [["sampledAtUtc", "varId", "value", "quality"], ...samples.map(sample => [sample.sampledAtUtc, sample.varId, sample.value, sample.quality])];
  const csv = "\\uFEFF" + rows.map(row => row.map(csvCell).join(",")).join("\\r\\n");
  const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = `HMIStudio-trend-${varId}-${new Date().toISOString().replace(/[:.]/g, "-")}.csv`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function formatTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleTimeString();
}

export default function TrendHistory({ onBack }: { onBack: () => void }) {
  const [variables, setVariables] = useState<TrendVariable[]>([]);
  const [selectedVarId, setSelectedVarId] = useState("");
  const [samples, setSamples] = useState<TrendSample[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null);

  const loadVariables = useCallback(async () => {
    try {
      const response = await fetch(`${API}/api/trends/variables`);
      if (!response.ok) throw new Error("无法读取趋势变量清单");
      const result = await response.json() as TrendVariable[];
      setVariables(result);
      setSelectedVarId(current => current && result.some(item => item.varId === current)
        ? current
        : result.find(item => item.varId === "line.speed")?.varId ?? result[0]?.varId ?? "");
      setError("");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "趋势变量加载失败");
    }
  }, []);

  const loadTrend = useCallback(async (varId: string) => {
    if (!varId) return;
    setLoading(true);
    setError("");
    try {
      const to = new Date();
      const from = new Date(to.getTime() - 60 * 60 * 1000);
      const query = new URLSearchParams({
        varId,
        from: from.toISOString(),
        to: to.toISOString(),
        limit: "3600",
      });
      const response = await fetch(`${API}/api/trends?${query.toString()}`);
      const result = await response.json() as TrendResponse | { error?: string };
      if (!response.ok) throw new Error("error" in result ? result.error ?? "趋势查询失败" : "趋势查询失败");
      setSamples((result as TrendResponse).samples);
      setUpdatedAt(new Date());
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "趋势查询失败");
      setSamples([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void loadVariables(); }, [loadVariables]);
  useEffect(() => { if (selectedVarId) void loadTrend(selectedVarId); }, [selectedVarId, loadTrend]);

  const numericSamples = useMemo(() => samples
    .map(sample => ({ ...sample, numericValue: sample.value === null ? Number.NaN : Number(sample.value) }))
    .filter(sample => Number.isFinite(sample.numericValue)), [samples]);

  const chart = useMemo(() => {
    const width = 900;
    const height = 280;
    const pad = { left: 58, right: 24, top: 24, bottom: 36 };
    const plotWidth = width - pad.left - pad.right;
    const plotHeight = height - pad.top - pad.bottom;
    const values = numericSamples.map(sample => sample.numericValue);
    const rawMin = values.length ? Math.min(...values) : 0;
    const rawMax = values.length ? Math.max(...values) : 1;
    const range = rawMax - rawMin || Math.max(Math.abs(rawMax) * 0.1, 1);
    const min = rawMin - range * 0.08;
    const max = rawMax + range * 0.08;
    const points = numericSamples.map((sample, index) => {
      const x = pad.left + (numericSamples.length <= 1 ? plotWidth / 2 : index / (numericSamples.length - 1) * plotWidth);
      const y = pad.top + (max - sample.numericValue) / (max - min) * plotHeight;
      return { x, y, sample };
    });
    return { width, height, pad, plotWidth, plotHeight, min, max, points, line: points.map(point => `${point.x},${point.y}`).join(" ") };
  }, [numericSamples]);

  const selected = variables.find(variable => variable.varId === selectedVarId);

  return (
    <div className="trend-page">
      <header className="trend-heading">
        <div>
          <button className="trend-back" onClick={onBack}><ArrowLeft size={15} />返回设备总览</button>
          <div className="eyebrow">HISTORICAL DATA / 历史数据</div>
          <h1>变量趋势</h1>
          <p>查看最近一小时保存的变量历史样本。数据来自本地模拟运行时。</p>
        </div>
        <div className="trend-heading-actions"><button className="trend-export" disabled={!samples.length || loading} onClick={() => exportSamples(selectedVarId, samples)}><Download size={15} />导出 CSV</button><button className="trend-refresh" disabled={loading} onClick={() => void loadTrend(selectedVarId)}><RefreshCw size={15} />{loading ? "查询中…" : "刷新趋势"}</button></div>
      </header>

      <section className="trend-summary">
        <div className="trend-select-wrap">
          <label htmlFor="trend-variable">监视变量</label>
          <select id="trend-variable" value={selectedVarId} onChange={event => setSelectedVarId(event.target.value)}>
            {variables.map(variable => <option key={variable.varId} value={variable.varId}>{variable.name} · {variable.varId}</option>)}
          </select>
        </div>
        <div className="trend-stat"><span>有效样本</span><strong>{numericSamples.length.toLocaleString()}</strong></div>
        <div className="trend-stat"><span>当前质量</span><strong className={selected?.quality === "Good" ? "trend-good" : "trend-bad"}>{selected?.quality ?? "—"}</strong></div>
        <div className="trend-stat"><span>更新时间</span><strong>{updatedAt ? updatedAt.toLocaleTimeString() : "—"}</strong></div>
      </section>

      {error && <div className="trend-error" role="alert">{error}<button onClick={() => void loadTrend(selectedVarId)}>重试</button></div>}

      <section className="trend-chart-panel">
        <div className="trend-panel-heading"><div><h2><TrendingUp size={18} />{selected?.name ?? "变量趋势"}</h2><p>{selectedVarId || "请选择变量"} · 最近 60 分钟 · 按采样时间升序</p></div><span className="trend-retention">保留 7 天</span></div>
        {numericSamples.length > 0 ? (
          <div className="trend-chart-scroll">
            <svg className="trend-chart" viewBox={`0 0 ${chart.width} ${chart.height}`} role="img" aria-label={`${selected?.name ?? selectedVarId} 历史趋势图`}>
              {[0, 1, 2, 3].map(index => {
                const y = chart.pad.top + index / 3 * chart.plotHeight;
                const value = chart.max - index / 3 * (chart.max - chart.min);
                return <g key={index}><line x1={chart.pad.left} x2={chart.width - chart.pad.right} y1={y} y2={y} className="trend-grid-line" /><text x={chart.pad.left - 10} y={y + 4} textAnchor="end" className="trend-axis-label">{value.toLocaleString(undefined, { maximumFractionDigits: 2 })}</text></g>;
              })}
              <line x1={chart.pad.left} x2={chart.pad.left} y1={chart.pad.top} y2={chart.height - chart.pad.bottom} className="trend-axis-line" />
              <line x1={chart.pad.left} x2={chart.width - chart.pad.right} y1={chart.height - chart.pad.bottom} y2={chart.height - chart.pad.bottom} className="trend-axis-line" />
              {chart.points.length > 1 && <polyline points={chart.line} className="trend-line" />}
              {chart.points.length === 1 && <circle cx={chart.points[0].x} cy={chart.points[0].y} r="4" className="trend-point" />}
              <text x={chart.pad.left} y={chart.height - 10} className="trend-axis-label">{formatTime(numericSamples[0].sampledAtUtc)}</text>
              <text x={chart.width - chart.pad.right} y={chart.height - 10} textAnchor="end" className="trend-axis-label">{formatTime(numericSamples[numericSamples.length - 1].sampledAtUtc)}</text>
            </svg>
          </div>
        ) : (
          <div className="trend-empty"><TrendingUp size={30} /><strong>{loading ? "正在读取趋势数据…" : "暂无数值趋势数据"}</strong><span>运行一段时间后刷新。布尔值或文本变量暂不绘制数值曲线。</span></div>
        )}
        <footer className="trend-chart-footer"><span>数据库：trends.sqlite</span><span>导出当前查询的样本（最多 3600 条）</span><span>历史记录自动保留 7 天</span></footer>
      </section>
    </div>
  );
}
