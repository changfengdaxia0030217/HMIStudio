import { useCallback, useEffect, useMemo, useState } from "react";
import { ArrowLeft, RefreshCw, Search, Wifi, WifiOff } from "lucide-react";

const API = (import.meta.env.VITE_API_BASE as string | undefined) ?? "";
type Quality = "Good" | "BadComm" | "BadNotExist" | "Uncertain" | string;
type Variable = {
  varId: string; name: string; dataType: string; min: number | null; max: number | null;
  writable: boolean; cycleMs: number; value: unknown; quality: Quality;
};
function showValue(value: unknown) {
  if (value === null || value === undefined) return "—";
  if (typeof value === "boolean") return value ? "TRUE" : "FALSE";
  if (typeof value === "number") return Number.isFinite(value) ? String(Number(value.toFixed(4))) : "—";
  if (typeof value === "object") return JSON.stringify(value);
  return String(value);
}
function qualityName(value: Quality) {
  if (value === "Good") return "通讯正常";
  if (value === "BadComm") return "通讯异常";
  if (value === "Uncertain") return "质量不确定";
  return "变量不存在";
}
export default function VariablesMonitor({ onBack }: { onBack: () => void }) {
  const [variables, setVariables] = useState<Variable[]>([]);
  const [query, setQuery] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [lastUpdate, setLastUpdate] = useState<Date | null>(null);
  const [online, setOnline] = useState(false);
  const [streamConnected, setStreamConnected] = useState(false);
  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await fetch(`${API}/api/variables`);
      if (!response.ok) throw new Error("变量接口返回异常状态");
      setVariables(await response.json() as Variable[]);
      setOnline(true); setError("");
    } catch {
      setOnline(false);
      setError("无法读取变量。请确认 HMIStudio 本机服务正在运行。");
    } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    if (online) return;
    const retry = window.setInterval(() => { void load(); }, 5000);
    return () => window.clearInterval(retry);
  }, [online, load]);
  useEffect(() => {
    if (!online || variables.length === 0) return;
    const ids = variables.map(v => v.varId);
    const stream = new EventSource(`${API}/api/runtime/stream?varIds=${encodeURIComponent(ids.join(","))}`);
    stream.onopen = () => setStreamConnected(true);
    stream.onerror = () => setStreamConnected(false);
    stream.addEventListener("variable", event => {
      try {
        const update = JSON.parse((event as MessageEvent).data) as {varId:string;value:unknown;quality:Quality;serverTimestamp:string};
        setVariables(prev => prev.map(v => v.varId === update.varId ? {...v,value:update.value,quality:update.quality} : v));
        setLastUpdate(new Date(update.serverTimestamp));
      } catch { /* Ignore malformed stream messages. */ }
    });
    return () => { stream.close(); setStreamConnected(false); };
  }, [online, variables.length]);
  const filtered = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase();
    if (!needle) return variables;
    return variables.filter(v => v.name.toLocaleLowerCase().includes(needle) || v.varId.toLocaleLowerCase().includes(needle) || v.dataType.toLocaleLowerCase().includes(needle));
  }, [variables, query]);
  const goodCount = variables.filter(v => v.quality === "Good").length;
  return <div className="alarm-page">
    <div className="alarm-page-heading"><div><div className="eyebrow">VARIABLE MONITOR / 变量监视</div><h1>变量监视</h1><p>实时查看工程变量值、数据类型、通讯质量与读写权限。</p></div>
      <div className="alarm-page-actions"><button className="alarm-secondary-button" onClick={onBack}><ArrowLeft size={14}/>返回运行总览</button><button className="alarm-secondary-button" onClick={() => void load()} disabled={loading}><RefreshCw size={14}/>{loading ? "刷新中…" : "刷新快照"}</button></div>
    </div>
    {error && <div className="alarm-message error"><WifiOff size={16}/>{error}</div>}
    <div className="alarm-summary-grid">
      <div className="alarm-summary-card"><span className="alarm-summary-icon history"><Search size={18}/></span><div><span>工程变量</span><strong>{variables.length}</strong><small>已加载变量总数</small></div></div>
      <div className="alarm-summary-card"><span className="alarm-summary-icon history"><Wifi size={18}/></span><div><span>通讯正常</span><strong>{goodCount}</strong><small>质量位为 Good</small></div></div>
      <div className="alarm-summary-card"><span className="alarm-summary-icon pending"><WifiOff size={18}/></span><div><span>质量异常</span><strong>{variables.length-goodCount}</strong><small>需检查驱动或变量状态</small></div></div>
    </div>
    <section className="panel alarm-table-panel">
      <div className="panel-heading"><div><h2>实时变量表</h2><p>实时订阅变量更新；值与质量位分别显示</p></div><span className="table-count">{filtered.length} / {variables.length} 个变量</span></div>
      <div className="variable-monitor-toolbar"><label className="variable-search"><Search size={15}/><input value={query} onChange={e => setQuery(e.target.value)} placeholder="搜索变量名称、ID 或类型"/></label><span className={streamConnected ? "variable-live good" : "variable-live"}><i/>{streamConnected ? "实时连接" : online ? "实时重连中" : "等待服务"}</span></div>
      <div className="table-wrap"><table><thead><tr><th>变量名称</th><th>变量 ID</th><th>数据类型</th><th>当前值</th><th>最小值</th><th>最大值</th><th>质量位</th><th>采样周期</th><th>权限</th></tr></thead><tbody>
        {filtered.map(v => <tr key={v.varId}><td className="variable-name">{v.name}</td><td><code>{v.varId}</code></td><td><span className="type-tag">{v.dataType}</span></td><td className="value-cell">{showValue(v.value)}</td><td>{v.min ?? "—"}</td><td>{v.max ?? "—"}</td><td><span className={`quality-tag ${v.quality === "Good" ? "q-good" : "q-bad"}`}><i/>{qualityName(v.quality)}</span></td><td>{v.cycleMs} ms</td><td>{v.writable ? <span className="writable">可写</span> : <span className="readonly">只读</span>}</td></tr>)}
        {!filtered.length && <tr><td colSpan={9} className="empty-state">{loading ? "正在读取变量…" : query ? "没有匹配的变量" : "工程中暂无变量"}</td></tr>}
      </tbody></table></div>
      <div className="table-footer"><span><Wifi size={14}/>{lastUpdate ? `最近更新：${lastUpdate.toLocaleTimeString()}` : "等待实时样本"}</span><span>变量值由本机模拟驱动提供</span></div>
    </section>
  </div>;
}
