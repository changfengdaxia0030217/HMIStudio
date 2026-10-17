import { useCallback, useEffect, useState } from "react";
import { ArrowLeft, ChevronLeft, ChevronRight, RefreshCw } from "lucide-react";
import { ControlRenderer, type ScreenControl, type VariableMap } from "./ControlRenderer";
import "./runtime-screen.css";

const API = (import.meta.env.VITE_API_BASE as string | undefined) ?? "";
type Screen = { id: string; title: string; background: string; width: number; height: number; controls: ScreenControl[] };
type ScreenSummary = { id: string; title: string };
type ProjectSummary = { startScreenId: string };
type Variable = { varId: string; name: string; value: unknown; quality?: string; writable?: boolean };

export default function RuntimeScreen({ onBack, variables }: { onBack: () => void; variables: Variable[] }) {
  const [screen, setScreen] = useState<Screen | null>(null);
  const [screenList, setScreenList] = useState<ScreenSummary[]>([]);
  const [selectedScreenId, setSelectedScreenId] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [navigationHistory, setNavigationHistory] = useState<string[]>([]);
  const [historyIndex, setHistoryIndex] = useState(-1);

  const loadScreen = useCallback(async (preferredId?: string): Promise<string | null> => {
    setError("");
    try {
      const [projectResponse, listResponse] = await Promise.all([
        fetch(`${API}/api/project`),
        fetch(`${API}/api/screens`),
      ]);
      if (!projectResponse.ok || !listResponse.ok) throw new Error("无法读取工程画面列表");
      const [project, listedScreens] = await Promise.all([
        projectResponse.json() as Promise<ProjectSummary>,
        listResponse.json() as Promise<ScreenSummary[]>,
      ]);
      const available = Array.isArray(listedScreens) ? listedScreens : [];
      setScreenList(available);
      const targetId = preferredId && available.some(item => item.id === preferredId)
        ? preferredId
        : available.some(item => item.id === project.startScreenId)
          ? project.startScreenId
          : available[0]?.id;
      if (!targetId) {
        setScreen(null);
        setSelectedScreenId("");
        throw new Error("工程中没有可运行的画面");
      }
      const response = await fetch(`${API}/api/screens/${encodeURIComponent(targetId)}`);
      if (!response.ok) throw new Error("无法读取运行画面");
      const value = await response.json() as Screen;
      setScreen(value);
      setSelectedScreenId(value.id);
      setError("");
      return value.id;
    } catch (e) {
      setError(e instanceof Error ? e.message : "画面加载失败");
      return null;
    }
  }, []);

  const navigateTo = useCallback(async (screenId: string) => {
    if (screenId === selectedScreenId) return;
    if (!screenList.some(item => item.id === screenId)) {
      setNotice(`跳转失败：工程中不存在画面 ${screenId}`);
      return;
    }
    const loadedId = await loadScreen(screenId);
    if (!loadedId) return;
    setNavigationHistory(previous => [...previous.slice(0, historyIndex + 1), loadedId]);
    setHistoryIndex(previous => previous + 1);
    setNotice("");
  }, [historyIndex, loadScreen, screenList, selectedScreenId]);

  const travelHistory = useCallback(async (nextIndex: number) => {
    const targetId = navigationHistory[nextIndex];
    if (!targetId || nextIndex === historyIndex) return;
    const loadedId = await loadScreen(targetId);
    if (loadedId) setHistoryIndex(nextIndex);
  }, [historyIndex, loadScreen, navigationHistory]);

  useEffect(() => {
    void loadScreen().then(loadedId => {
      if (loadedId) {
        setNavigationHistory([loadedId]);
        setHistoryIndex(0);
      }
    });
  }, [loadScreen]);

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

  const stageWidth = screen?.width ?? 1280;
  const stageHeight = screen?.height ?? 720;

  return <div className="runtime-screen-shell">
    <header className="runtime-screen-header">
      <button onClick={onBack}><ArrowLeft size={16} />返回监控</button>
      <div><strong>{screen?.title ?? "运行画面"}</strong><span>运行模式 · 共享控件渲染器</span></div>
      {screenList.length > 1 && <label className="runtime-screen-picker">画面
        <select aria-label="选择运行画面" value={selectedScreenId} onChange={e => void navigateTo(e.target.value)}>
          {screenList.map(item => <option key={item.id} value={item.id}>{item.title}</option>)}
        </select>
      </label>}
      <div className="runtime-screen-history" aria-label="画面导航历史">
        <button type="button" aria-label="上一画面" title="上一画面" disabled={historyIndex <= 0} onClick={() => void travelHistory(historyIndex - 1)}><ChevronLeft size={16} />上一画面</button>
        <button type="button" aria-label="下一画面" title="下一画面" disabled={historyIndex < 0 || historyIndex >= navigationHistory.length - 1} onClick={() => void travelHistory(historyIndex + 1)}>下一画面<ChevronRight size={16} /></button>
      </div>
      <button onClick={() => void loadScreen(selectedScreenId || undefined)}><RefreshCw size={15} />刷新画面</button>
    </header>
    {error && <div className="runtime-screen-error">{error}</div>}
    {notice && <div className="runtime-screen-notice" role="status">{notice}<button onClick={() => setNotice("")}>关闭</button></div>}
    <main className="runtime-screen-content"><div className="runtime-screen-stage" style={{ background: screen?.background ?? "#111827", width: `min(100%, ${stageWidth}px)`, aspectRatio: `${stageWidth} / ${stageHeight}` }}>
      {screen?.controls.map(control => <ControlRenderer key={control.id} control={control} variables={byId} runtime onWrite={(varId, value) => { void writeVariable(varId, value); }} onNavigate={screenId => { void navigateTo(screenId); }} />)}
      {!screen && !error && <div className="runtime-screen-loading">正在加载工程画面…</div>}
    </div></main>
    <footer className="runtime-screen-footer"><span>{screen?.controls.length ?? 0} 个控件</span><span>{busy ? "正在写入变量…" : "按钮可写入变量 · 数值输入在离开输入框时提交"}</span></footer>
  </div>;
}
