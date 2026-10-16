import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, Box, Circle, CircleDot, Eye, Grid2X2, Lamp, MousePointer2, Plus, RectangleHorizontal, Save, ShieldCheck, Trash2, Type, Variable as VariableIcon, ZoomIn, ZoomOut } from "lucide-react";
import "./editor.css";
import "./runtime-screen.css";
import { ControlRenderer, resolveControlProperty, type VariableMap } from "./ControlRenderer";
import { CONTROL_REGISTRY, getControlDefinition } from "./ControlPropertyRegistry";

const API = (import.meta.env.VITE_API_BASE as string | undefined) ?? "";
type Source = { type: "$static"; value?: unknown } | { type: "$var"; varId: string; index?: number } | { type: string; [key: string]: unknown };
type Binding = { source?: Source | null; direction?: "OneWay" | "TwoWay"; dependencies?: string[]; mapping?: unknown[] };
type Control = { id: string; name: string; controlType: string; x: number; y: number; width: number; height: number; properties: Record<string, Binding>; eventActions?: Record<string, unknown[]> };
type Screen = { id: string; title: string; background: string; screenType: string; width: number; height: number; controls: Control[] };
type Variable = { varId: string; name: string; dataType: string; value: unknown; writable: boolean };
const ICONS = { Text: Type, Button: MousePointer2, Lamp, NumericDisplay: VariableIcon, NumericInput: CircleDot, Rectangle: RectangleHorizontal } as const;
const LIBRARY = CONTROL_REGISTRY.map(item => ({ ...item, icon: ICONS[item.type as keyof typeof ICONS] }));
const fallbackScreen: Screen = { id: "main", title: "设备总览", background: "#111827", screenType: "Normal", width: 1280, height: 720, controls: [] };
function validateScreenBindings(screen: Screen, variables: Variable[], screens: Array<{ id: string; title: string }>): string[] {
  const variableIds = new Set(variables.map(variable => variable.varId));
  const screenIds = new Set(screens.map(item => item.id));
  const errors: string[] = [];
  for (const control of screen.controls) {
    for (const [key, binding] of Object.entries(control.properties ?? {})) {
      const source = binding?.source;
      if (source?.type === "$var") {
        const varId = String(source.varId ?? "").trim();
        if (!varId) errors.push(`控件“${control.name}”的属性“${key}”未选择变量。`);
        else if (!variableIds.has(varId)) errors.push(`控件“${control.name}”的属性“${key}”引用了不存在的变量“${varId}”。`);
      }
    }
    const writeSource = control.properties?.writeVarId?.source;
    if (writeSource?.type === "$static") {
      const varId = String(writeSource.value ?? "").trim();
      if (varId && !variableIds.has(varId)) errors.push(`按钮“${control.name}”写入了不存在的变量“${varId}”。`);
    }
    const navigationSource = control.properties?.navigateToScreenId?.source;
    if (navigationSource?.type === "$static") {
      const screenId = String(navigationSource.value ?? "").trim();
      if (screenId && !screenIds.has(screenId)) errors.push(`控件“${control.name}”跳转到不存在的画面“${screenId}”。`);
    }
  }
  return errors;
}
const staticValue = (control: Control, key: string, fallback: unknown) => {
  const source = control.properties?.[key]?.source;
  if (source?.type === "$static") return source.value ?? fallback;
  if (source?.type === "$var") return source.varId;
  return fallback;
};
function newControl(type: string, index: number): Control {
  const definition = getControlDefinition(type);
  const props: Record<string, Binding> = {};
  for (const property of definition.properties) {
    props[property.key] = { source: { type: "$static", value: property.defaultValue } };
  }
  return {
    id: `${type.toLowerCase()}-${Date.now().toString(36)}-${index}`,
    name: definition.label, controlType: type,
    x: 100 + index * 18, y: 100 + index * 18,
    width: definition.width, height: definition.height,
    properties: props, eventActions: {},
  };
}
export default function Editor({ onBack }: { onBack: () => void }) {
  const [screen, setScreen] = useState<Screen>(fallbackScreen);
  const [screenList, setScreenList] = useState<Array<{ id: string; title: string }>>([]);
  const [startScreenId, setStartScreenId] = useState("main");
  const [variables, setVariables] = useState<Variable[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [message, setMessage] = useState("");
  const [zoom, setZoom] = useState(1);
  const [hasClipboard, setHasClipboard] = useState(false);
  const [, setHistoryVersion] = useState(0);
  const historyRef = useRef<{ past: Screen[]; future: Screen[] }>({ past: [], future: [] });
  const stageRef = useRef<HTMLDivElement>(null);
  const liveScreenRef = useRef(screen);
  liveScreenRef.current = screen;
  const clipboardRef = useRef<Control | null>(null);
  const selected = screen.controls.find(c => c.id === selectedId) ?? null;
  const variablesById: VariableMap = useMemo(() => Object.fromEntries(variables.map(v => [v.varId, v])), [variables]);
  const booleanVariables = useMemo(() => variables.filter(variable => ["bool", "boolean"].includes(variable.dataType.toLowerCase())), [variables]);
  const load = useCallback(async (preferredScreenId?: string) => {
    setLoading(true);
    try {
      const [projectResponse, screensResponse, variablesResponse] = await Promise.all([
        fetch(`${API}/api/project`), fetch(`${API}/api/screens`), fetch(`${API}/api/variables`),
      ]);
      if (!projectResponse.ok || !screensResponse.ok || !variablesResponse.ok) throw new Error("本机运行时不可用，请先启动 HMIStudio.Server。");
      const [project, listedScreens, loadedVariables] = await Promise.all([projectResponse.json(), screensResponse.json(), variablesResponse.json()]);
      const available = Array.isArray(listedScreens) ? listedScreens as Array<{ id: string; title: string }> : [];
      setScreenList(available);
      setStartScreenId(project.startScreenId ?? "");
      const targetId = preferredScreenId && available.some(item => item.id === preferredScreenId)
        ? preferredScreenId
        : available.some(item => item.id === project.startScreenId)
          ? project.startScreenId
          : available[0]?.id;
      if (!targetId) throw new Error("工程中没有可编辑的画面。");
      const screenResponse = await fetch(`${API}/api/screens/${encodeURIComponent(targetId)}`);
      if (!screenResponse.ok) throw new Error("无法读取所选画面。");
      const loadedScreen = await screenResponse.json();
      const loaded = { ...loadedScreen, controls: loadedScreen.controls ?? [] } as Screen;
      liveScreenRef.current = loaded; setScreen(loaded); historyRef.current = { past: [], future: [] }; setHistoryVersion(v => v + 1);
      setVariables(loadedVariables);
      setSelectedId(null); setSelectedIds([]); setDirty(false); setMessage("");
    } catch (e) { setMessage(e instanceof Error ? e.message : "工程加载失败"); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!dirty) return;
    const protectUnsavedChanges = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", protectUnsavedChanges);
    return () => window.removeEventListener("beforeunload", protectUnsavedChanges);
  }, [dirty]);
  const commitScreen = (next: Screen) => {
    historyRef.current.past.push(JSON.parse(JSON.stringify(liveScreenRef.current)) as Screen);
    if (historyRef.current.past.length > 100) historyRef.current.past.shift();
    historyRef.current.future = [];
    liveScreenRef.current = next; setScreen(next); setDirty(true); setHistoryVersion(v => v + 1);
  };
  const patchControl = (id: string, patch: Partial<Control>) => {
    commitScreen({ ...screen, controls: screen.controls.map(c => c.id === id ? { ...c, ...patch } : c) });
  };
  const patchProperty = (key: string, value: unknown) => {
    if (!selected) return;
    const properties = { ...selected.properties, [key]: { ...(selected.properties[key] ?? {}), source: { type: "$static", value } } as Binding };
    patchControl(selected.id, { properties });
  };
  const addControl = (type: string) => {
    const control = newControl(type, screen.controls.length);
    commitScreen({ ...screen, controls: [...screen.controls, control] });
    setSelectedId(control.id); setSelectedIds([control.id]);
  };
  const removeSelected = () => {
    const ids = selectedIds.length ? selectedIds : selected ? [selected.id] : [];
    if (!ids.length) return;
    const removed = new Set(ids);
    commitScreen({ ...screen, controls: screen.controls.filter(c => !removed.has(c.id)) });
    setSelectedId(null); setSelectedIds([]);
  };
  const setStartScreen = async () => {
    if (dirty) {
      setMessage("请先保存当前画面，再修改工程启动画面。");
      return;
    }
    if (screen.id === startScreenId) return;
    setSaving(true); setMessage("");
    try {
      const response = await fetch(`${API}/api/project/start-screen`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ screenId: screen.id }),
      });
      const result = await response.json();
      if (!response.ok || !result.ok) throw new Error(result.error ?? "设置启动画面失败");
      setStartScreenId(screen.id);
      setMessage("已将当前画面设为工程启动画面。");
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "设置启动画面失败");
    } finally {
      setSaving(false);
    }
  };
  const deleteScreen = async () => {
    if (dirty) {
      setMessage("请先保存当前画面，再删除画面。");
      return;
    }
    if (screenList.length <= 1 || screen.id === startScreenId) {
      setMessage("不能删除工程中的最后一个画面或启动画面。");
      return;
    }
    if (!window.confirm(`确定删除画面“${screen.title}”吗？此操作会保存到工程文件。`)) return;
    setSaving(true); setMessage("");
    try {
      const response = await fetch(`${API}/api/screens/${encodeURIComponent(screen.id)}`, { method: "DELETE" });
      const result = await response.json();
      if (!response.ok || !result.ok) throw new Error(result.error ?? "删除画面失败");
      await load(startScreenId);
      setMessage("画面已删除。");
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "删除画面失败");
    } finally {
      setSaving(false);
    }
  };
  const createScreen = async () => {
    if (dirty) {
      setMessage("请先保存当前画面，再新建画面，避免丢失未保存的修改。");
      return;
    }
    setSaving(true); setMessage("");
    const id = `screen-${Date.now().toString(36)}`;
    const title = `新建画面 ${screenList.length + 1}`;
    try {
      const response = await fetch(`${API}/api/screens`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ id, title, background: "#111827", width: 1280, height: 720 }),
      });
      const result = await response.json();
      if (!response.ok || !result.ok) throw new Error(result.error ?? "新建画面失败");
      await load(id);
      if (liveScreenRef.current.id === id) setMessage("新画面已创建并载入。");
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "新建画面失败");
    } finally {
      setSaving(false);
    }
  };
  const validateProject = async () => {
    if (dirty) {
      setMessage("请先保存当前画面，再校验工程。");
      return;
    }
    setSaving(true); setMessage("");
    try {
      const response = await fetch(`${API}/api/project/validate`);
      const result = await response.json() as { ok: boolean; errors: string[]; detail?: string; error?: string };
      if (!response.ok) throw new Error(result.detail ?? result.error ?? "工程校验请求失败");
      setMessage(result.ok ? "工程校验通过：未发现结构问题。" : `工程校验发现 ${result.errors.length} 个问题：${result.errors.join("；")}`);
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "工程校验失败");
    } finally { setSaving(false); }
  };
  const save = async () => {
    const bindingErrors = validateScreenBindings(screen, variables, screenList);
    if (bindingErrors.length) {
      setMessage(`保存前发现 ${bindingErrors.length} 个变量/画面绑定问题：${bindingErrors.slice(0, 3).join("；")}${bindingErrors.length > 3 ? "；请修复其余问题后重试" : ""}`);
      return;
    }
    setSaving(true); setMessage("");
    try {
      const response = await fetch(`${API}/api/screens/${encodeURIComponent(screen.id)}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(screen) });
      const result = await response.json();
      if (!response.ok) throw new Error(result.error ?? "保存失败");
      const verifyResponse = await fetch(`${API}/api/screens/${encodeURIComponent(screen.id)}`);
      if (!verifyResponse.ok) throw new Error("保存请求已返回，但重新读取画面失败");
      const persisted = await verifyResponse.json() as Screen;
      const verified = { ...persisted, controls: persisted.controls ?? [] }; liveScreenRef.current = verified; setScreen(verified); historyRef.current = { past: [], future: [] }; setHistoryVersion(v => v + 1);
      setDirty(false); setMessage("工程已保存，并已重新读取验证。");
    } catch (e) { setMessage(e instanceof Error ? e.message : "保存失败"); }
    finally { setSaving(false); }
  };
  const undo = () => {
    const previous = historyRef.current.past.pop();
    if (!previous) return;
    historyRef.current.future.push(JSON.parse(JSON.stringify(liveScreenRef.current)) as Screen);
    liveScreenRef.current = previous; setScreen(previous); setDirty(true); setHistoryVersion(v => v + 1);
    setSelectedId(id => previous.controls.some(item => item.id === id) ? id : null);
    setSelectedIds(ids => ids.filter(id => previous.controls.some(item => item.id === id)));
  };
  const redo = () => {
    const next = historyRef.current.future.pop();
    if (!next) return;
    historyRef.current.past.push(JSON.parse(JSON.stringify(liveScreenRef.current)) as Screen);
    liveScreenRef.current = next; setScreen(next); setDirty(true); setHistoryVersion(v => v + 1);
    setSelectedId(id => next.controls.some(item => item.id === id) ? id : null);
    setSelectedIds(ids => ids.filter(id => next.controls.some(item => item.id === id)));
  };
  const copySelected = () => { if (selected) { clipboardRef.current = JSON.parse(JSON.stringify(selected)) as Control; setHasClipboard(true); } };
  const alignSelected = (axis: "left" | "top" | "center-x" | "center-y") => {
    const chosen = screen.controls.filter(control => selectedIds.includes(control.id));
    if (chosen.length < 2) return;
    const anchor = chosen[0];
    const updated = new Map<string, Control>();
    for (const control of chosen) {
      let patch: Partial<Control>;
      if (axis === "left") patch = { x: anchor.x };
      else if (axis === "top") patch = { y: anchor.y };
      else if (axis === "center-x") patch = { x: Math.max(0, Math.min(1280 - control.width, Math.round((anchor.x + anchor.width / 2 - control.width / 2) / 8) * 8)) };
      else patch = { y: Math.max(0, Math.min(720 - control.height, Math.round((anchor.y + anchor.height / 2 - control.height / 2) / 8) * 8)) };
      updated.set(control.id, { ...control, ...patch });
    }
    commitScreen({ ...screen, controls: screen.controls.map(control => updated.get(control.id) ?? control) });
  };
  const pasteControl = () => {
    const source = clipboardRef.current; if (!source) return;
    const clone = JSON.parse(JSON.stringify(source)) as Control;
    clone.id = `${clone.controlType.toLowerCase()}-${Date.now().toString(36)}-${screen.controls.length}`;
    clone.name = `${source.name} 副本`; clone.x = Math.min(1280 - clone.width, source.x + 24); clone.y = Math.min(720 - clone.height, source.y + 24);
    commitScreen({ ...screen, controls: [...screen.controls, clone] }); setSelectedId(clone.id); setSelectedIds([clone.id]);
  };
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (target && (target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName))) return;
      const mod = event.ctrlKey || event.metaKey;
      if (mod && event.key.toLowerCase() === "z") { event.preventDefault(); event.shiftKey ? redo() : undo(); }
      else if (mod && event.key.toLowerCase() === "y") { event.preventDefault(); redo(); }
      else if (mod && event.key.toLowerCase() === "c" && selected) { event.preventDefault(); copySelected(); }
      else if (mod && event.key.toLowerCase() === "v" && clipboardRef.current) { event.preventDefault(); pasteControl(); }
      else if ((event.key === "Delete" || event.key === "Backspace") && (selectedIds.length || selected)) { event.preventDefault(); removeSelected(); }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  });
  const binding = selected?.properties?.text?.source;
  const bindingMode = binding?.type === "$var" ? "variable" : "static";
  const textValue = String(resolveControlProperty(selected ?? newControl("Text", 0), "text", variablesById, selected?.name ?? ""));
  const stageStyle = useMemo(() => ({ background: screen.background || "#111827" }), [screen.background]);
  return <div className="editor-shell">
    <header className="editor-topbar">
      <button className="editor-back" onClick={() => { if (saving) return; if (dirty && !window.confirm("当前画面有未保存的修改。确定放弃修改并返回运行监控吗？")) return; onBack(); }}><ArrowLeft size={16} />返回运行监控</button>
      <div className="editor-project"><strong>HMIStudio</strong><span>/ 画面组态编辑器</span><i className={dirty ? "dirty-dot" : ""} />{dirty ? "未保存" : "已同步"}</div>
      <div className="editor-actions"><button className="editor-ghost" disabled={!historyRef.current.past.length} onClick={undo} title="撤销 Ctrl+Z">↶</button><button className="editor-ghost" disabled={!historyRef.current.future.length} onClick={redo} title="重做 Ctrl+Y">↷</button><button className="editor-ghost" disabled={!selected} onClick={copySelected} title="复制 Ctrl+C">复制</button><button className="editor-ghost" disabled={!hasClipboard} onClick={pasteControl} title="粘贴 Ctrl+V">粘贴</button><button className="editor-ghost" disabled={selectedIds.length < 2} onClick={() => alignSelected("left")} title="多选控件左对齐">左对齐</button><button className="editor-ghost" disabled={selectedIds.length < 2} onClick={() => alignSelected("top")} title="多选控件顶对齐">顶对齐</button><button className="editor-ghost" disabled={selectedIds.length < 2} onClick={() => alignSelected("center-x")} title="多选控件水平中心对齐">水平居中</button><button className="editor-ghost" disabled={selectedIds.length < 2} onClick={() => alignSelected("center-y")} title="多选控件垂直中心对齐">垂直居中</button><button className="editor-ghost" onClick={() => setZoom(z => Math.max(.5, +(z - .1).toFixed(1)))} title="缩小"><ZoomOut size={15} /></button><span>{Math.round(zoom * 100)}%</span><button className="editor-ghost" onClick={() => setZoom(z => Math.min(1.5, +(z + .1).toFixed(1)))} title="放大"><ZoomIn size={15} /></button><button className="editor-ghost" disabled={saving || loading || dirty} onClick={() => void validateProject()} title={dirty ? "请先保存修改再校验" : "检查工程 JSON 结构"}><ShieldCheck size={15} />校验工程</button><button className="editor-save" disabled={saving || loading || !dirty} onClick={() => void save()}><Save size={15} />{saving ? "保存中" : "保存工程"}</button></div>
    </header>
    <div className="editor-workspace">
      <aside className="editor-library">
        <div className="editor-pane-title"><Box size={15} />控件库</div>
        <p>选择控件添加到画布</p>
        <div className="editor-library-grid">{LIBRARY.map(item => <button key={item.type} className="editor-library-item" onClick={() => addControl(item.type)}><item.icon size={20} /><span>{item.label}</span><Plus size={13} /></button>)}</div>
        <div className="editor-library-note"><Grid2X2 size={15} /><span>画布基准尺寸<br /><b>1280 × 720</b></span></div>
      </aside>
      <main className="editor-center">
        <div className="editor-canvas-toolbar"><div><span className="editor-screen-dot" />{screenList.length > 1 ? <select className="editor-screen-select" aria-label="选择要编辑的画面" value={screen.id} onChange={e => { if (dirty) { setMessage("请先保存当前画面，再切换画面，避免丢失未保存的修改。"); return; } void load(e.target.value); }}>{screenList.map(item => <option key={item.id} value={item.id}>{item.title || item.id}</option>)}</select> : (screen.title || "未命名画面")}<small>/{screen.id}</small></div><div><button className="editor-new-screen" disabled={loading || saving} onClick={() => void createScreen()}><Plus size={13} />新建画面</button><button className="editor-delete-screen" disabled={loading || saving || dirty || screenList.length <= 1 || screen.id === startScreenId} onClick={() => void deleteScreen()} title="不能删除启动画面、最后一个画面或未保存的画面"><Trash2 size={13} />删除画面</button>{screen.id === startScreenId ? <span className="editor-start-screen current">启动画面</span> : <button className="editor-start-screen" disabled={loading || saving} onClick={() => void setStartScreen()} title="将当前画面设置为工程启动画面">设为启动画面</button>}<Eye size={14} />设计预览</div></div>
        <div className="editor-canvas-scroll">
          <div className="editor-stage-wrap" style={{ width: `${zoom * 100}%` }}>
            <div ref={stageRef} className="editor-stage" style={stageStyle} onPointerDown={e => { if (e.target === e.currentTarget) { setSelectedId(null); setSelectedIds([]); } }}>
              <div className="editor-grid" />
              {screen.controls.map(control => <ControlRenderer key={control.id} control={control} variables={variablesById} selected={selectedIds.includes(control.id)} interactive onPointerDown={event => {
                event.stopPropagation();
                if (event.shiftKey) {
                  const nextIds = selectedIds.includes(control.id) ? selectedIds.filter(id => id !== control.id) : [...selectedIds, control.id];
                  setSelectedIds(nextIds);
                  setSelectedId(nextIds.includes(control.id) ? control.id : nextIds[nextIds.length - 1] ?? null);
                  return;
                }
                setSelectedId(control.id); setSelectedIds([control.id]);
                const startX = event.clientX, startY = event.clientY;
                const before = JSON.parse(JSON.stringify(liveScreenRef.current)) as Screen;
                const original = before.controls.find(item => item.id === control.id) ?? control;
                const target = event.target as HTMLElement;
                const handle = target.classList.contains("hmi-control-handle") ? target.className.match(/handle-(nw|n|ne|e|se|s|sw|w)/)?.[1] : undefined;
                const stage = stageRef.current; if (!stage) return;
                const rect = stage.getBoundingClientRect(); let moved = false; let finalControl = original;
                const move = (pointer: PointerEvent) => {
                  const dx = (pointer.clientX - startX) / rect.width * 1280, dy = (pointer.clientY - startY) / rect.height * 720;
                  if (Math.abs(dx) + Math.abs(dy) > 1) moved = true;
                  if (handle) {
                    let x = original.x, y = original.y, right = original.x + original.width, bottom = original.y + original.height;
                    if (handle.includes("w")) x = Math.max(0, Math.min(right - 24, Math.round((original.x + dx) / 8) * 8));
                    if (handle.includes("e")) right = Math.min(1280, Math.max(x + 24, Math.round((original.x + original.width + dx) / 8) * 8));
                    if (handle.includes("n")) y = Math.max(0, Math.min(bottom - 24, Math.round((original.y + dy) / 8) * 8));
                    if (handle.includes("s")) bottom = Math.min(720, Math.max(y + 24, Math.round((original.y + original.height + dy) / 8) * 8));
                    finalControl = { ...original, x, y, width: right - x, height: bottom - y };
                  } else {
                    finalControl = { ...original, x: Math.max(0, Math.min(1280 - original.width, Math.round((original.x + dx) / 8) * 8)), y: Math.max(0, Math.min(720 - original.height, Math.round((original.y + dy) / 8) * 8)) };
                  }
                  const next = { ...liveScreenRef.current, controls: liveScreenRef.current.controls.map(item => item.id === control.id ? finalControl : item) };
                  liveScreenRef.current = next; setScreen(next);
                };
                const up = () => {
                  window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); window.removeEventListener("pointercancel", up);
                  if (moved) { historyRef.current.past.push(before); if (historyRef.current.past.length > 100) historyRef.current.past.shift(); historyRef.current.future = []; setDirty(true); setHistoryVersion(v => v + 1); }
                  else { liveScreenRef.current = before; setScreen(before); }
                };
                window.addEventListener("pointermove", move); window.addEventListener("pointerup", up, { once: true }); window.addEventListener("pointercancel", up, { once: true });
              }} />)}
              {!screen.controls.length && <div className="editor-empty">从左侧控件库添加控件<br /><small>拖动控件调整位置，右侧修改属性</small></div>}
            </div>
          </div>
        </div>
        <footer className="editor-statusbar"><span><Circle size={8} />{screen.controls.length} 个控件</span><span>{selectedIds.length > 1 ? `已多选 ${selectedIds.length} 个` : "Shift + 点击可多选"}</span><span>吸附网格：8 px</span><span>{dirty ? "有未保存修改" : "所有修改已保存"}</span></footer>
      </main>
      <aside className="editor-inspector">
        <div className="editor-pane-title">属性检查器</div>
        {!selected ? <>
          <div className="screen-inspector-title"><strong>画面属性</strong><small>当前画面</small></div>
          <label className="editor-field"><span>画面名称</span><input value={screen.title} onChange={e => commitScreen({ ...screen, title: e.target.value })} /></label>
          <label className="editor-field"><span>背景颜色</span><div className="editor-color-row"><input type="color" value={/^#[0-9a-f]{6}$/i.test(screen.background) ? screen.background : "#111827"} onChange={e => commitScreen({ ...screen, background: e.target.value })} /><span>{screen.background}</span></div></label>
          <div className="inspector-empty"><MousePointer2 size={22} /><strong>未选择控件</strong><span>选择画面中的控件后<br />可编辑其位置、外观与变量绑定</span></div>
          <div className="inspector-hint"><Grid2X2 size={14}/><span>画面名称和背景颜色会随“保存工程”写入 JSON 工程文件。</span></div>
        </> : <>
          <div className="inspector-control-title"><span>{selected.controlType}</span><small>{selected.id}</small></div>
          <label className="editor-field"><span>控件名称</span><input value={selected.name} onChange={e => patchControl(selected.id, { name: e.target.value })} /></label>
          <div className="editor-field-grid">
            {(["x", "y", "width", "height"] as const).map(key => <label className="editor-field" key={key}><span>{{ x: "X 坐标", y: "Y 坐标", width: "宽度", height: "高度" }[key]}</span><input type="number" min={key === "width" || key === "height" ? 8 : 0} value={selected[key]} onChange={e => patchControl(selected.id, { [key]: Math.max(key === "width" || key === "height" ? 8 : 0, Number(e.target.value) || 0) })} /></label>)}
          </div>
          {getControlDefinition(selected.controlType).properties.map(property => {
            const value = String(staticValue(selected, property.key, property.defaultValue));
            if (property.key === "writeVarId") {
              return <label className="editor-field" key={property.key}><span>{property.label}</span><select value={value} onChange={e => patchProperty(property.key, e.target.value)}><option value="">不写入变量</option>{variables.map(variable => <option key={variable.varId} value={variable.varId}>{variable.name} · {variable.varId}</option>)}</select></label>;
            }
            if (property.kind === "text") {
              const text = property.key === "text" ? textValue : value;
              return <label className="editor-field" key={property.key}><span>{property.label}</span><input value={text} onChange={e => patchProperty(property.key, e.target.value)} /></label>;
            }
            if (property.kind === "color") {
              const colorSource = selected.properties?.[property.key]?.source;
              const colorIsVariable = colorSource?.type === "$var";
              const color = /^#[0-9a-f]{6}$/i.test(value) ? value : String(property.defaultValue);
              return <div className="editor-field" key={property.key}>
                <span>{property.label}</span>
                <select aria-label={`${property.label}来源`} value={colorIsVariable ? "variable" : "static"} onChange={e => {
                  if (e.target.value === "variable") {
                    patchControl(selected.id, { properties: { ...selected.properties, [property.key]: { ...(selected.properties[property.key] ?? {}), source: { type: "$var", varId: booleanVariables[0]?.varId ?? "" }, direction: "OneWay" } } });
                  } else {
                    patchProperty(property.key, colorIsVariable ? String(property.defaultValue) : value);
                  }
                }}>
                  <option value="static">固定颜色</option>
                  <option value="variable" disabled={!booleanVariables.length}>绑定布尔变量（驱动指示灯颜色）</option>
                </select>
                {colorIsVariable
                  ? <label className="editor-field"><span>颜色绑定变量</span><select disabled={!booleanVariables.length} value={String(colorSource?.type === "$var" ? colorSource.varId : "")} onChange={e => patchControl(selected.id, { properties: { ...selected.properties, [property.key]: { ...(selected.properties[property.key] ?? {}), source: { type: "$var", varId: e.target.value }, direction: "OneWay" } } })}><option value="">{booleanVariables.length ? "请选择布尔变量" : "暂无布尔变量"}</option>{booleanVariables.map(v => <option key={v.varId} value={v.varId}>{v.name} · {v.varId}</option>)}</select>{!booleanVariables.length && <small>请先创建 bool 类型变量，才能用变量驱动颜色。</small>}</label>
                  : <div className="editor-color-row"><input type="color" value={color} onChange={e => patchProperty(property.key, e.target.value)} /><input aria-label={property.label} value={value} onChange={e => patchProperty(property.key, e.target.value)} /></div>}
              </div>;
            }
            return <label className="editor-field" key={property.key}><span>{property.label}</span><input type="number" min={property.min} max={property.max} step={property.step ?? 1} value={Number(resolveControlProperty(selected, property.key, variablesById, property.defaultValue))} onChange={e => patchProperty(property.key, Math.max(property.min ?? Number.MIN_SAFE_INTEGER, Math.min(property.max ?? Number.MAX_SAFE_INTEGER, Number(e.target.value) || Number(property.defaultValue))))} /></label>;
          })}
          {getControlDefinition(selected.controlType).properties.some(property => property.key === "text") && <>
            <label className="editor-field"><span>文本来源</span><select value={bindingMode} onChange={e => {
              if (e.target.value === "variable") patchControl(selected.id, { properties: { ...selected.properties, text: { source: { type: "$var", varId: variables[0]?.varId ?? "" }, direction: "OneWay" } } });
              else patchProperty("text", textValue);
            }}><option value="static">静态值</option><option value="variable">绑定变量</option></select></label>
            {bindingMode === "variable" && <label className="editor-field"><span>绑定变量</span><select disabled={!variables.length} value={String(binding?.type === "$var" ? binding.varId : "")} onChange={e => patchControl(selected.id, { properties: { ...selected.properties, text: { source: { type: "$var", varId: e.target.value }, direction: "OneWay" } } })}><option value="">{variables.length ? "请选择变量" : "暂无可用变量"}</option>{variables.map(v => <option key={v.varId} value={v.varId}>{v.name} · {v.varId}</option>)}</select>{!variables.length && <small>请先在变量管理中创建变量，再启用动态绑定。</small>}</label>}
          </>}
          <div className="inspector-hint"><VariableIcon size={14} /><span>变量绑定会在运行画面实时求值；按钮可配置变量写入，数值输入可绑定可写变量。</span></div>
          <button className="editor-delete" onClick={removeSelected}>删除此控件</button>
        </>}
      </aside>
    </div>
    {message && <div className={`editor-toast ${message.includes("失败") || message.includes("不可用") || message.includes("问题") || message.includes("不存在") || message.includes("不能为空") ? "error" : ""} `} role="status">{message}<button onClick={() => setMessage("")}>关闭</button></div>}
  </div>;
}
function colorValue(control: Control, key: string, fallback: string) { return String(staticValue(control, key, fallback)); }
