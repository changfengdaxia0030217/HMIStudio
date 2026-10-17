import { useCallback, useEffect, useState } from "react";
import { AlertTriangle, Check, Clock3, Download, RefreshCw, ShieldAlert } from "lucide-react";

const API = (import.meta.env.VITE_API_BASE as string | undefined) ?? "";

type ActiveAlarm = {
  alarmId: string; ruleId: string; ruleName: string; varId: string;
  message: string | null; value: string | null; triggeredAt: string;
  acknowledged: boolean; acknowledgedAt: string | null;
};
type AlarmEvent = {
  sequence: number; alarmId: string; ruleId: string; ruleName: string; varId: string;
  eventType: string; message: string | null; value: string | null; quality: string;
  occurredAt: string;
};

function time(value: string | null | undefined) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
function eventLabel(value: string) {
  switch (value.toLowerCase()) {
    case "triggered": return "触发";
    case "acknowledged": return "已确认";
    case "recovered": return "已恢复";
    default: return value || "未知事件";
  }
}

export default function AlarmCenter({ onBack }: { onBack: () => void }) {
  const [active, setActive] = useState<ActiveAlarm[]>([]);
  const [history, setHistory] = useState<AlarmEvent[]>([]);
  const [historyEventType, setHistoryEventType] = useState("");
  const [historySearchInput, setHistorySearchInput] = useState("");
  const [historySearch, setHistorySearch] = useState("");
  const [loading, setLoading] = useState(false);
  const [busyId, setBusyId] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = useCallback(async (showSpinner = true) => {
    if (showSpinner) setLoading(true);
    try {
      const [activeResponse, historyResponse] = await Promise.all([
        fetch(`${API}/api/alarms/active`),
        fetch(`${API}/api/alarms/history?${new URLSearchParams({ limit: "200", ...(historyEventType ? { eventType: historyEventType } : {}), ...(historySearch ? { search: historySearch } : {}) })}`),
      ]);
      if (!activeResponse.ok || !historyResponse.ok) throw new Error("报警接口返回异常状态");
      const [activeItems, historyItems] = await Promise.all([activeResponse.json(), historyResponse.json()]);
      setActive(activeItems as ActiveAlarm[]);
      setHistory(historyItems as AlarmEvent[]);
      setError("");
    } catch {
      setError("无法读取报警数据，请确认 HMIStudio 本机服务正在运行。");
    } finally {
      if (showSpinner) setLoading(false);
    }
  }, [historyEventType, historySearch]);

  useEffect(() => {
    void load();
    const interval = window.setInterval(() => { void load(false); }, 5000);
    return () => window.clearInterval(interval);
  }, [load]);

  const acknowledge = async (alarmId: string) => {
    setBusyId(alarmId); setNotice(""); setError("");
    try {
      const response = await fetch(`${API}/api/alarms/${encodeURIComponent(alarmId)}/acknowledge`, { method: "POST" });
      const result = await response.json();
      if (!response.ok || !result.ok) throw new Error(result.error ?? "报警确认失败");
      setNotice("报警已确认。");
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "报警确认失败");
    } finally {
      setBusyId("");
    }
  };

  return (
    <div className="alarm-page">
      <div className="alarm-page-heading">
        <div>
          <div className="eyebrow">ALARM MANAGEMENT / 报警管理</div>
          <h1>报警中心</h1>
          <p>查看活动报警、确认待处理报警，并追溯 SQLite 持久化的报警事件。</p>
        </div>
        <div className="alarm-page-actions">
          <button className="alarm-secondary-button" onClick={onBack}>返回运行总览</button>
          <button className="alarm-secondary-button" onClick={() => { const query = new URLSearchParams({ limit: "1000", ...(historyEventType ? { eventType: historyEventType } : {}), ...(historySearch ? { search: historySearch } : {}) }); window.location.href = `${API}/api/alarms/history.csv?${query}`; }}><Download size={14} />导出当前筛选 CSV</button>
          <button className="alarm-secondary-button" onClick={() => void load()} disabled={loading}><RefreshCw size={14} />{loading ? "刷新中…" : "刷新"}</button>
        </div>
      </div>

      {error && <div className="alarm-message error"><ShieldAlert size={16} />{error}</div>}
      {notice && <div className="alarm-message success"><Check size={16} />{notice}</div>}

      <div className="alarm-summary-grid">
        <div className="alarm-summary-card"><span className="alarm-summary-icon active"><AlertTriangle size={18} /></span><div><span>活动报警</span><strong>{active.length}</strong><small>尚未恢复的报警</small></div></div>
        <div className="alarm-summary-card"><span className="alarm-summary-icon history"><Clock3 size={18} /></span><div><span>历史事件</span><strong>{history.length}</strong><small>当前显示最近 200 条</small></div></div>
        <div className="alarm-summary-card"><span className="alarm-summary-icon pending"><ShieldAlert size={18} /></span><div><span>待确认</span><strong>{active.filter(item => !item.acknowledged).length}</strong><small>需要操作员确认</small></div></div>
      </div>

      <section className="panel alarm-table-panel">
        <div className="panel-heading"><div><h2>活动报警</h2><p>报警恢复前持续显示；确认不会清除活动状态</p></div><span className="table-count">{active.length} 条</span></div>
        <div className="table-wrap"><table><thead><tr><th>状态</th><th>报警名称</th><th>变量</th><th>当前值</th><th>触发时间</th><th>确认时间</th><th>操作</th></tr></thead><tbody>
          {active.map(item => <tr key={item.alarmId}>
            <td><span className={item.acknowledged ? "alarm-state acknowledged" : "alarm-state pending"}>{item.acknowledged ? "已确认" : "待确认"}</span></td>
            <td><div className="alarm-name-cell">{item.ruleName}<small>{item.message || item.ruleId}</small></div></td>
            <td><code>{item.varId}</code></td><td className="value-cell">{item.value ?? "—"}</td><td>{time(item.triggeredAt)}</td><td>{time(item.acknowledgedAt)}</td>
            <td>{item.acknowledged ? <span className="alarm-done">已处理</span> : <button className="alarm-ack-button" disabled={busyId === item.alarmId} onClick={() => void acknowledge(item.alarmId)}>{busyId === item.alarmId ? "提交中…" : "确认报警"}</button>}</td>
          </tr>)}
          {!active.length && <tr><td colSpan={7} className="empty-state">{loading ? "正在读取活动报警…" : "当前没有活动报警"}</td></tr>}
        </tbody></table></div>
      </section>

      <section className="panel alarm-table-panel">
        <div className="panel-heading"><div><h2>报警事件历史</h2><p>按发生时间倒序显示触发、确认与恢复事件</p></div><span className="table-count">{history.length} 条</span></div>
        <div className="alarm-history-filters">
          <label>事件类型
            <select value={historyEventType} onChange={event => setHistoryEventType(event.target.value)}>
              <option value="">全部事件</option><option value="Triggered">触发</option><option value="Acknowledged">已确认</option><option value="Recovered">已恢复</option>
            </select>
          </label>
          <form onSubmit={event => { event.preventDefault(); setHistorySearch(historySearchInput.trim()); }}>
            <label>关键词
              <input value={historySearchInput} onChange={event => setHistorySearchInput(event.target.value)} placeholder="报警名称、变量、消息…" />
            </label>
            <button className="alarm-secondary-button" type="submit">查询</button>
            <button className="alarm-secondary-button" type="button" onClick={() => { setHistorySearchInput(""); setHistorySearch(""); setHistoryEventType(""); }}>清除</button>
          </form>
        </div>
        <div className="table-wrap"><table><thead><tr><th>序号</th><th>事件</th><th>报警名称</th><th>变量</th><th>记录值</th><th>质量</th><th>发生时间</th></tr></thead><tbody>
          {history.map(item => <tr key={item.sequence}>
            <td className="alarm-sequence">{item.sequence}</td><td><span className={`alarm-event-tag event-${item.eventType.toLowerCase()}`}>{eventLabel(item.eventType)}</span></td>
            <td><div className="alarm-name-cell">{item.ruleName}<small>{item.message || item.ruleId}</small></div></td>
            <td><code>{item.varId}</code></td><td className="value-cell">{item.value ?? "—"}</td><td>{item.quality}</td><td>{time(item.occurredAt)}</td>
          </tr>)}
          {!history.length && <tr><td colSpan={7} className="empty-state">{loading ? "正在读取报警历史…" : "暂无报警历史记录"}</td></tr>}
        </tbody></table></div>
        <div className="table-footer"><span><Check size={14} />历史由本机 SQLite 保存</span><span>最多显示 200 条匹配记录</span></div>
      </section>
    </div>
  );
}
