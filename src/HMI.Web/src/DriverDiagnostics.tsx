import { useCallback, useEffect, useMemo, useState } from "react";
import { Activity, AlertTriangle, CheckCircle2, Cpu, RefreshCw, ShieldAlert, Wifi, WifiOff } from "lucide-react";

const API = (import.meta.env.VITE_API_BASE as string | undefined) ?? "";

type DriverVariable = {
  varId: string;
  name: string;
  cycleMs: number;
  address: string | null;
  quality: string;
};
type Driver = {
  protocol: string;
  driverType: string;
  status: string;
  variableCount: number;
  goodVariableCount: number;
  badVariableCount: number;
  variables: DriverVariable[];
};

function statusLabel(status: string) {
  switch (status.toLowerCase()) {
    case "connected": return "已连接";
    case "initialized": return "已初始化";
    case "disconnected": return "已断开";
    default: return status || "未知";
  }
}
function statusClass(status: string) {
  switch (status.toLowerCase()) {
    case "connected": return "connected";
    case "initialized": return "initialized";
    default: return "disconnected";
  }
}

export default function DriverDiagnostics({ onBack }: { onBack: () => void }) {
  const [drivers, setDrivers] = useState<Driver[]>([]);
  const [serviceOnline, setServiceOnline] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async (showSpinner = true) => {
    if (showSpinner) setLoading(true);
    try {
      const [driverResponse, healthResponse] = await Promise.all([
        fetch(`${API}/api/drivers`),
        fetch(`${API}/api/health`),
      ]);
      if (!driverResponse.ok || !healthResponse.ok) throw new Error("诊断接口返回异常状态");
      const [driverItems, health] = await Promise.all([driverResponse.json(), healthResponse.json()]);
      setDrivers(Array.isArray(driverItems) ? driverItems as Driver[] : []);
      setServiceOnline(health.status === "ok");
      setError("");
    } catch {
      setError("无法读取驱动诊断信息，请确认 HMIStudio 本机服务正在运行。");
      setServiceOnline(false);
    } finally {
      if (showSpinner) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
    const interval = window.setInterval(() => { void load(false); }, 5000);
    return () => window.clearInterval(interval);
  }, [load]);

  const summary = useMemo(() => drivers.reduce((result, driver) => ({
    driverCount: result.driverCount + 1,
    variableCount: result.variableCount + driver.variableCount,
    goodCount: result.goodCount + driver.goodVariableCount,
    badCount: result.badCount + driver.badVariableCount,
  }), { driverCount: 0, variableCount: 0, goodCount: 0, badCount: 0 }), [drivers]);

  return (
    <div className="driver-page">
      <div className="driver-page-heading">
        <div>
          <div className="eyebrow">DRIVER DIAGNOSTICS / 驱动诊断</div>
          <h1>驱动诊断</h1>
          <p>查看已配置模拟驱动的连接状态、映射变量和通讯质量。诊断页面每 5 秒自动刷新。</p>
        </div>
        <div className="driver-page-actions">
          <button onClick={onBack}>返回运行总览</button>
          <button onClick={() => void load()} disabled={loading}><RefreshCw size={14}/>{loading ? "刷新中…" : "刷新"}</button>
        </div>
      </div>

      {error && <div className="alarm-message error"><ShieldAlert size={16}/>{error}</div>}

      <div className="driver-summary-grid">
        <div className="driver-summary-card"><span className="driver-summary-icon"><Cpu size={19}/></span><div><span>配置驱动</span><strong>{summary.driverCount}</strong><small>按 PLC 通道分组</small></div></div>
        <div className="driver-summary-card"><span className="driver-summary-icon"><Activity size={19}/></span><div><span>映射变量</span><strong>{summary.variableCount}</strong><small>已配置 PLC 变量</small></div></div>
        <div className="driver-summary-card"><span className="driver-summary-icon good"><CheckCircle2 size={19}/></span><div><span>通讯正常</span><strong>{summary.goodCount}</strong><small>质量位 Good</small></div></div>
        <div className="driver-summary-card"><span className="driver-summary-icon bad"><AlertTriangle size={19}/></span><div><span>质量异常</span><strong>{summary.badCount}</strong><small>需要检查的变量</small></div></div>
      </div>

      <section className="panel driver-table-panel">
        <div className="panel-heading"><div><h2>驱动连接状态</h2><p>首版仅启用本地模拟 PLC，不会连接真实设备</p></div><span className={serviceOnline ? "connection good" : "connection bad"}>{serviceOnline ? <Wifi size={14}/> : <WifiOff size={14} />}{serviceOnline ? "运行时在线" : "服务未连接"}</span></div>
        <div className="table-wrap"><table><thead><tr><th>协议 / 通道</th><th>驱动类型</th><th>连接状态</th><th>映射变量</th><th>通讯正常</th><th>质量异常</th></tr></thead><tbody>
          {drivers.map(driver => <tr key={driver.protocol}>
            <td><strong>{driver.protocol}</strong></td><td>{driver.driverType}</td><td><span className={`driver-status ${statusClass(driver.status)}`}>{statusLabel(driver.status)}</span></td>
            <td className="driver-variable-count">{driver.variableCount}</td><td>{driver.goodVariableCount}</td><td>{driver.badVariableCount}</td>
          </tr>)}
          {!drivers.length && <tr><td colSpan={6} className="empty-state">{loading ? "正在读取驱动…" : "当前工程没有配置 PLC 驱动变量"}</td></tr>}
        </tbody></table></div>
      </section>

      <section className="panel driver-table-panel">
        <div className="panel-heading"><div><h2>变量映射明细</h2><p>从工程变量元数据读取通道地址、采样周期与质量位</p></div><span className="table-count">{summary.variableCount} 个变量</span></div>
        <div className="table-wrap"><table><thead><tr><th>变量名称</th><th>变量 ID</th><th>通道地址</th><th>质量位</th><th>采样周期</th></tr></thead><tbody>
          {drivers.flatMap(driver => driver.variables.map(variable => <tr key={variable.varId}>
            <td className="variable-name">{variable.name}</td><td><code>{variable.varId}</code></td><td><span className="driver-address">{variable.address || "—"}</span></td>
            <td><span className={`quality-tag ${variable.quality === "Good" ? "q-good" : "q-bad"}`}><i/>{variable.quality === "Good" ? "通讯正常" : variable.quality}</span></td><td>{variable.cycleMs} ms</td>
          </tr>))}
          {!summary.variableCount && <tr><td colSpan={5} className="empty-state">没有 PLC 变量映射</td></tr>}
        </tbody></table></div>
        <div className="table-footer"><span><CheckCircle2 size={14}/>诊断数据来自当前工程配置</span><span>刷新间隔：5 秒</span></div>
      </section>

      <div className="driver-note"><ShieldAlert size={16}/><span>当前状态仅用于模拟运行与工程配置检查。真实 PLC 协议驱动、网络连通性测试和设备级诊断将在后续版本接入。</span></div>
    </div>
  );
}
