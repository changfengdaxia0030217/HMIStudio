import { useEffect, useState } from "react";
import { ArrowLeft, RefreshCw } from "lucide-react";
import { ControlRenderer, type ScreenControl, type VariableMap } from "./ControlRenderer";
import "./runtime-screen.css";

const API = (import.meta.env.VITE_API_BASE as string | undefined) ?? "http://127.0.0.1:5080";
type Screen = { id: string; title: string; background: string; width: number; height: number; controls: ScreenControl[] };
type Variable = { varId: string; name: string; value: unknown; quality?: string; writable?: boolean };
export default function RuntimeScreen({ onBack, variables }: { onBack: () => void; variables: Variable[] }) {
  const [screen, setScreen] = useState<Screen | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let active = true;
    fetch(`${API}/api/screens/main`).then(async response => {
      if (!response.ok) throw new Error("无法读取运行画面");
      return response.json() as Promise<Screen>;
    }).then(value => { if (active) { setScreen(value); setError(""); } })
      .catch(e => { if (active) setError(e instanceof Error ? e.message : "画面加载失败"); });
    return () => { active = false; };
  }, []);
  const byId: VariableMap = Object.fromEntries(variables.map(v => [v.varId, v]));
  const writeVariable = async (varId: string, value: unknown) => {
    if (busy) return;
    setBusy(true); setNotice("");
    try {
      const response = await fetch(API + "/api/variables/" + encodeURIComponent(varId) + "/write", {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ value, requestId: crypto.randomUUID() }),
      });
      const result = await response.json();
      if (!response.ok || !result.ok) throw new Error(result.message ?? result.Message ?? "变量写入失败");
      setNotice(varId + " 写入成功");
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "变量写入失败");
    } finally { setBusy(false); }
  };
  return <div className="runtime-screen-shell">
    <header className="runtime-screen-header"><button onClick={onBack}><ArrowLeft size={16} />返回监控</button><div><strong>{screen?.title ?? "运行画面"}</strong><span>运行模式 · 共享控件渲染器</span></div><button onClick={() => { setScreen(null); setError(""); fetch(`${API}/api/screens/main`).then(r => { if (!r.ok) throw new Error("画面加载失败"); return r.json(); }).then(setScreen).catch(e => setError(e instanceof Error ? e.message : "画面加载失败")); }}><RefreshCw size={15} />刷新画面</button></header>
    {error && <div className="runtime-screen-error">{error}</div>}
    {notice && <div className="runtime-screen-notice" role="status">{notice}<button onClick={() => setNotice("")}>关闭</button></div>}
    <main className="runtime-screen-content"><div className="runtime-screen-stage" style={{ background: screen?.background ?? "#111827" }}>
      {screen?.controls.map(control => <ControlRenderer key={control.id} control={control} variables={byId} runtime onWrite={(varId, value) => { void writeVariable(varId, value); }} />)}
      {!screen && !error && <div className="runtime-screen-loading">正在加载工程画面…</div>}
    </div></main>
    <footer className="runtime-screen-footer"><span>{screen?.controls.length ?? 0} 个控件</span><span>{busy ? "正在写入变量…" : "按钮可写入变量 · 数值输入在离开输入框时提交"}</span></footer>
  </div>;
}
