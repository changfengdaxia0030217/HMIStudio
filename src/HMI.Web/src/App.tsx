import { useCallback, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import Editor from "./Editor";
import RuntimeScreen from "./RuntimeScreen";
import AlarmCenter from "./AlarmCenter";
import VariablesMonitor from "./VariablesMonitor";
import DriverDiagnostics from "./DriverDiagnostics";
import {
  Activity, AlertTriangle, ArrowDownRight, ArrowUpRight, Box, CheckCircle2,
  CircleHelp, Cpu, Gauge, RefreshCw, Send, Server, ShieldCheck,
  SlidersHorizontal, Wifi, WifiOff, Zap,
} from "lucide-react";

const API = (import.meta.env.VITE_API_BASE as string | undefined) ?? "";

type Quality = "Good" | "BadComm" | "BadNotExist" | "Uncertain" | string;
type Variable = {
  varId: string; name: string; dataType: string; min: number | null; max: number | null;
  writable: boolean; cycleMs: number; value: unknown; quality: Quality;
};
type ProjectSummary = {
  version: string; formatVersion: number; startScreenId: string; baseWidth: number;
  baseHeight: number; projectPath: string; screenCount: number; variableCount: number;
  runtimeMode: string; uptimeSeconds: number;
};

function displayValue(value: unknown) {
  if (typeof value === "boolean") return value ? "运行 / TRUE" : "停止 / FALSE";
  if (value === null || value === undefined) return "—";
  if (typeof value === "number") return Number.isInteger(value) ? String(value) : value.toFixed(1);
  return String(value);
}
function formatUptime(seconds = 0) {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  return [h, m, s].map(v => String(v).padStart(2, "0")).join(":");
}
function qualityLabel(q: Quality) {
  return q === "Good" ? "通讯正常" : q === "BadComm" ? "通讯异常" : q === "Uncertain" ? "质量不确定" : "变量不存在";
}

export default function App() {
  const [view, setView] = useState<"runtime" | "editor" | "screen" | "alarms" | "variables" | "drivers">("runtime");
  const [project, setProject] = useState<ProjectSummary | null>(null);
  const [variables, setVariables] = useState<Variable[]>([]);
  const [online, setOnline] = useState(false);
  const [error, setError] = useState("");
  const [lastUpdate, setLastUpdate] = useState<Date | null>(null);
  const [streamConnected, setStreamConnected] = useState(false);
  const [targetSpeed, setTargetSpeed] = useState("55");
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const [activeAlarmCount, setActiveAlarmCount] = useState(0);

  const load = useCallback(async () => {
    try {
      const [healthResponse, projectResponse, variableResponse] = await Promise.all([
        fetch(`${API}/api/health`), fetch(`${API}/api/project`), fetch(`${API}/api/variables`),
      ]);
      if (!healthResponse.ok || !projectResponse.ok || !variableResponse.ok) throw new Error("服务返回异常状态");
      const [health, p, v] = await Promise.all([healthResponse.json(), projectResponse.json(), variableResponse.json()]);
      setOnline(health.status === "ok");
      setProject(p);
      setVariables(v);
      const target = v.find((x: Variable) => x.varId === "line.targetSpeed");
      if (target) setTargetSpeed(String(target.value ?? 55));
      setError("");
    } catch {
      setOnline(false);
      setError("无法连接本机运行时。请先启动 HMIStudio.Server（127.0.0.1:5080）。");
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  // The event stream is the primary live path; periodic snapshots recover from proxy,
  // browser sleep, or transient SSE disconnects without resetting operator input fields.
  useEffect(() => {
    if (!online) return;
    let cancelled = false;
    const refreshSnapshot = async () => {
      try {
        const response = await fetch(`${API}/api/variables`);
        if (!response.ok) return;
        const snapshot = await response.json() as Variable[];
        if (cancelled || !Array.isArray(snapshot)) return;
        setVariables(previous => {
          const priorById = new Map(previous.map(item => [item.varId, item]));
          return snapshot.map(item => {
            const prior = priorById.get(item.varId);
            // Keep a newer streamed sample if it arrived after this snapshot request began.
            return prior && lastUpdate && prior.varId === item.varId
              ? { ...item, value: prior.value, quality: prior.quality }
              : item;
          });
        });
      } catch {
        // The main connection indicator is driven by /api/health; SSE reports its own status.
      }
    };
    const interval = window.setInterval(() => { void refreshSnapshot(); }, 10000);
    return () => { cancelled = true; window.clearInterval(interval); };
  }, [online, lastUpdate]);

  const refreshAlarmCount = useCallback(async () => {
    try {
      const response = await fetch(`${API}/api/alarms/active`);
      if (!response.ok) throw new Error("报警接口不可用");
      const activeAlarms = await response.json();
      setActiveAlarmCount(Array.isArray(activeAlarms) ? activeAlarms.length : 0);
    } catch {
      // Keep the overview usable if the alarm endpoint is temporarily unavailable.
    }
  }, []);

  useEffect(() => {
    if (!online) {
      setActiveAlarmCount(0);
      return;
    }
    void refreshAlarmCount();
    const interval = window.setInterval(() => { void refreshAlarmCount(); }, 5000);
    return () => window.clearInterval(interval);
  }, [online, refreshAlarmCount]);

  const variableIds = useMemo(() => variables.map(v => v.varId).join(","), [variables]);

  useEffect(() => {
    if (!online || !variableIds) {
      setStreamConnected(false);
      return;
    }
    const stream = new EventSource(`${API}/api/runtime/stream?varIds=${encodeURIComponent(variableIds)}`);
    stream.onopen = () => setStreamConnected(true);
    stream.addEventListener("ready", () => setStreamConnected(true));
    stream.addEventListener("variable", (event) => {
      try {
        const update = JSON.parse((event as MessageEvent).data) as { varId: string; value: unknown; quality: Quality; serverTimestamp: string };
        setVariables(previous => previous.map(v => v.varId === update.varId
          ? { ...v, value: update.value, quality: update.quality }
          : v));
        setLastUpdate(new Date(update.serverTimestamp));
        setStreamConnected(true);
      } catch { /* Ignore a malformed sample and keep the stream alive. */ }
    });
    stream.onerror = () => setStreamConnected(false);
    return () => stream.close();
  }, [online, variableIds]);

  const byId = useMemo(() => Object.fromEntries(variables.map(v => [v.varId, v])) as Record<string, Variable>, [variables]);
  const goodCount = variables.filter(v => v.quality === "Good").length;
  const alarm = Boolean(byId["line.alarm"]?.value);
  const running = Boolean(byId["line.running"]?.value);
  const speed = Number(byId["line.speed"]?.value ?? 0);

  if (view === "editor") return <Editor onBack={() => setView("runtime")} />;
  if (view === "screen") return <RuntimeScreen onBack={() => setView("runtime")} variables={variables} />;
  if (view === "alarms") return <AlarmCenter onBack={() => setView("runtime")} />;
  if (view === "variables") return <VariablesMonitor onBack={() => setView("runtime")} />;
  if (view === "drivers") return <DriverDiagnostics onBack={() => setView("runtime")} />;

  const write = async (varId: string, value: unknown, message: string) => {
    setBusy(true); setNotice("");
    try {
      const response = await fetch(`${API}/api/variables/${encodeURIComponent(varId)}/write`, {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ value, requestId: crypto.randomUUID() }),
      });
      const receipt = await response.json();
      if (!response.ok || !receipt.ok) throw new Error(receipt.message ?? "写入失败");
      setNotice(message);
      await load();
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "写入失败");
    } finally { setBusy(false); }
  };

  const inject = async (address: string, value: unknown) => {
    setBusy(true); setNotice("");
    try {
      const response = await fetch(`${API}/api/runtime/mock-values`, {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ protocol: "MOCK", address, value }),
      });
      const result = await response.json();
      if (!response.ok) throw new Error(result.error ?? "模拟值更新失败");
      setNotice("模拟信号已注入，实时值将自动刷新。");
    } catch (e) { setNotice(e instanceof Error ? e.message : "模拟值更新失败"); }
    finally { setBusy(false); }
  };

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand-mark"><Activity size={22} strokeWidth={2.5} /></div>
        <div className="brand-copy"><strong>HMIStudio</strong><span>INDUSTRIAL STUDIO</span></div>
        <div className="side-divider" />
        <div className="nav-caption">运行中心</div>
        <div className="nav-item active"><Gauge size={17} />设备总览</div>
        <div className="nav-item editor-nav-item" role="button" tabIndex={0} onClick={() => setView("variables")} onKeyDown={e => { if (e.key === "Enter" || e.key === " ") setView("variables"); }}><SlidersHorizontal size={17} />变量监视</div>
        <div className="nav-item editor-nav-item" role="button" tabIndex={0} onClick={() => setView("screen")} onKeyDown={e => { if (e.key === "Enter" || e.key === " ") setView("screen"); }}><Activity size={17} />运行画面</div>
        <div className="nav-item editor-nav-item" role="button" tabIndex={0} onClick={() => setView("editor")} onKeyDown={e => { if (e.key === "Enter" || e.key === " ") setView("editor"); }}><Box size={17} />组态编辑</div>
        <div className="nav-item editor-nav-item" role="button" tabIndex={0} onClick={() => setView("alarms")} onKeyDown={e => { if (e.key === "Enter" || e.key === " ") setView("alarms"); }}><AlertTriangle size={17} />报警中心 <span className="nav-count">{activeAlarmCount}</span></div>
        <div className="nav-item editor-nav-item" role="button" tabIndex={0} onClick={() => setView("drivers")} onKeyDown={e => { if (e.key === "Enter" || e.key === " ") setView("drivers"); }}><Cpu size={17} />驱动诊断</div>
        <div className="sidebar-bottom">
          <div className="security-note"><ShieldCheck size={16} /><span>本机模拟运行环境<br /><small>未连接真实 PLC</small></span></div>
          <div className="sidebar-version">HMIStudio Preview · 0.1.0</div>
        </div>
      </aside>

      <main className="main-area">
        <header className="topbar">
          <div className="breadcrumb">工程 <span>/</span> <strong>设备运行总览</strong></div>
          <div className="topbar-right">
            <span className={online ? "connection good" : "connection bad"}>{online ? <Wifi size={15} /> : <WifiOff size={15} />}{online ? "本机服务已连接" : "服务未连接"}</span>
            <button className="icon-button" onClick={() => void load()} title="刷新数据"><RefreshCw size={16} /></button>
            <div className="avatar">HS</div>
          </div>
        </header>

        <section className="content">
          <div className="page-heading">
            <div><div className="eyebrow">RUNTIME MONITOR / 运行监控</div><h1>设备运行总览</h1><p>监视设备状态、实时变量与模拟信号。当前所有 PLC 数据均来自本地模拟驱动。</p></div>
            <div className="heading-status"><span className={online ? "status-dot" : "status-dot offline"} />{online ? "运行时在线" : "等待连接"}<span className="status-divider" />{project ? `工程格式 v${project.formatVersion}` : "加载工程中"}</div>
          </div>

          {error && <div className="error-banner"><CircleHelp size={18} /><span>{error}</span><button onClick={() => void load()}>重试</button></div>}
          {notice && <div className="notice-banner">{notice}<button onClick={() => setNotice("")}>关闭</button></div>}

          <div className="metric-grid">
            <Metric icon={<Activity size={18} />} label="设备状态" value={running ? "运行中" : "已停止"} detail={running ? "设备处于运行状态" : "设备当前停止"} tone={running ? "green" : "slate"} />
            <Metric icon={<Gauge size={18} />} label="当前速度" value={`${Number.isFinite(speed) ? speed.toFixed(1) : "—"}`} unit="Hz" detail="模拟 PLC 实时采样" tone="blue" />
            <Metric icon={<AlertTriangle size={18} />} label="活动报警" value={String(activeAlarmCount)} detail={activeAlarmCount > 0 ? `${activeAlarmCount} 条活动报警待处理或跟踪` : "当前无活动报警"} tone={activeAlarmCount > 0 ? "amber" : "green"} />
            <Metric icon={<Zap size={18} />} label="变量通讯" value={`${goodCount}/${variables.length}`} detail="质量位为 Good 的变量" tone={goodCount === variables.length && variables.length > 0 ? "green" : "blue"} />
          </div>

          <div className="section-grid">
            <section className="panel process-panel">
              <div className="panel-heading"><div><h2>过程监视</h2><p>实时过程变量与通讯质量</p></div><span className="live-pill"><i /> LIVE</span></div>
              <div className="process-visual">
                <div className="pipe pipe-top" /><div className="pipe pipe-bottom" />
                <div className={`tank ${running ? "tank-running" : ""}`}><div className="tank-liquid" /><div className="tank-label">T-101</div><span className="tank-level">{Math.min(100, Math.max(0, speed))}%</span></div>
                <div className="flow-arrow"><ArrowDownRight size={18} /></div>
                <div className="pump"><div className="pump-inner"><Zap size={23} /></div><span>P-101</span></div>
                <div className="pipe pipe-right" />
                <div className="process-callout"><span>当前速度</span><strong>{Number.isFinite(speed) ? speed.toFixed(1) : "—"} <small>Hz</small></strong><em className={byId["line.speed"]?.quality === "Good" ? "quality-good" : "quality-bad"}>{qualityLabel(byId["line.speed"]?.quality ?? "BadComm")}</em></div>
              </div>
              <div className="panel-foot"><span><i className="legend-dot green-dot" />运行状态</span><span><i className="legend-dot blue-dot" />模拟数据</span><span className="sample-time">{streamConnected ? "实时流已连接" : "实时流重连中"} · 最近采样：{lastUpdate ? lastUpdate.toLocaleTimeString() : "等待数据"}</span></div>
            </section>

            <section className="panel control-panel">
              <div className="panel-heading"><div><h2>操作控制</h2><p>仅用于模拟环境验证</p></div><span className="panel-icon"><SlidersHorizontal size={17} /></span></div>
              <div className="control-row"><div className="control-label"><span>设备运行</span><small>line.running · Memory</small></div><button disabled={busy || !online} className={`toggle ${running ? "on" : ""}`} onClick={() => void write("line.running", !running, running ? "已发送停止命令。" : "已发送启动命令。")}><span />{running ? "运行中" : "已停止"}</button></div>
              <div className="control-row target-control"><div className="control-label"><span>目标速度</span><small>line.targetSpeed · Memory</small></div><div className="target-input"><input type="number" min="0" max="100" value={targetSpeed} onChange={e => setTargetSpeed(e.target.value)} /><span>Hz</span><button disabled={busy || !online} onClick={() => void write("line.targetSpeed", Number(targetSpeed), "目标速度已写入。")}><Send size={14} /></button></div></div>
              <div className="control-divider" />
              <div className="control-label"><span>模拟信号注入</span><small>改变模拟 PLC 的寄存器值</small></div>
              <div className="signal-actions"><button disabled={busy || !online} onClick={() => void inject("line.speed", Math.min(100, speed + 5))}><ArrowUpRight size={15} />速度 +5</button><button disabled={busy || !online} onClick={() => void inject("line.speed", Math.max(0, speed - 5))}><ArrowDownRight size={15} />速度 -5</button><button disabled={busy || !online} className={alarm ? "alarm-active" : ""} onClick={() => void inject("line.alarm", !alarm)}><AlertTriangle size={15} />{alarm ? "清除报警" : "触发报警"}</button></div>
              <div className="simulation-warning"><ShieldCheck size={15} /><span>这些命令只影响模拟变量，不会控制真实设备。</span></div>
            </section>
          </div>

          <section className="panel variables-panel">
            <div className="panel-heading"><div><h2>变量列表</h2><p>工程变量 · 值与质量位分离 · 按周期更新</p></div><span className="table-count">{variables.length} 个变量</span></div>
            <div className="table-wrap"><table><thead><tr><th>变量名称</th><th>变量 ID</th><th>数据类型</th><th>当前值</th><th>质量位</th><th>周期</th><th>权限</th></tr></thead><tbody>
              {variables.map(v => <tr key={v.varId}><td className="variable-name">{v.name}</td><td><code>{v.varId}</code></td><td><span className="type-tag">{v.dataType}</span></td><td className="value-cell">{displayValue(v.value)}</td><td><span className={`quality-tag ${v.quality === "Good" ? "q-good" : "q-bad"}`}><i />{qualityLabel(v.quality)}</span></td><td>{v.cycleMs} ms</td><td>{v.writable ? <span className="writable">可写</span> : <span className="readonly">只读</span>}</td></tr>)}
              {!variables.length && <tr><td colSpan={7} className="empty-state">正在读取工程变量…</td></tr>}
            </tbody></table></div>
            <div className="table-footer"><span><CheckCircle2 size={14} />实时订阅已启用</span><span>接口：{API}</span><button onClick={() => void load()}>刷新快照 <RefreshCw size={13} /></button></div>
          </section>

          <footer className="page-footer"><span>HMIStudio · 工业人机界面平台</span><span>本地运行 · 模拟驱动 · 尚未连接真实 PLC</span></footer>
        </section>
      </main>
    </div>
  );
}

function Metric({ icon, label, value, unit, detail, tone }: { icon: ReactNode; label: string; value: string; unit?: string; detail: string; tone: string }) {
  return <div className="metric-card"><div className={`metric-icon ${tone}`}>{icon}</div><div className="metric-label">{label}</div><div className="metric-value">{value}{unit && <small>{unit}</small>}</div><div className="metric-detail">{detail}</div></div>;
}
