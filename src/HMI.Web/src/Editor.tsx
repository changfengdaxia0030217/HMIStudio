import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, Box, Circle, CircleDot, Eye, Grid2X2, Lamp, MousePointer2, Plus, RectangleHorizontal, Save, Type, Variable as VariableIcon, ZoomIn, ZoomOut } from "lucide-react";
import "./editor.css";
import "./runtime-screen.css";
import { ControlRenderer, resolveControlProperty, type VariableMap } from "./ControlRenderer";
import { CONTROL_REGISTRY, getControlDefinition } from "./ControlPropertyRegistry";

const API = (import.meta.env.VITE_API_BASE as string | undefined) ?? "http://127.0.0.1:5080";
type Source = { type: "$static"; value?: unknown } | { type: "$var"; varId: string; index?: number } | { type: string; [key: string]: unknown };
type Binding = { source?: Source | null; direction?: "OneWay" | "TwoWay"; dependencies?: string[]; mapping?: unknown[] };
type Control = { id: string; name: string; controlType: string; x: number; y: number; width: number; height: number; properties: Record<string, Binding>; eventActions?: Record<string, unknown[]> };
type Screen = { id: string; title: string; background: string; screenType: string; width: number; height: number; controls: Control[] };
type Variable = { varId: string; name: string; dataType: string; value: unknown; writable: boolean };
const ICONS = { Text: Type, Button: MousePointer2, Lamp, NumericDisplay: VariableIcon, NumericInput: CircleDot, Rectangle: RectangleHorizontal } as const;
const LIBRARY = CONTROL_REGISTRY.map(item => ({ ...item, icon: ICONS[item.type as keyof typeof ICONS] }));
const fallbackScreen: Screen = { id: "main", title: "设备总览", background: "#111827", screenType: "Normal", width: 1280, height: 720, controls: [] };
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
  const [variables, setVariables] = useState<Variable[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [message, setMessage] = useState("");
  const [zoom, setZoom] = useState(1);
  const [, setHistoryVersion] = useState(0);
  const historyRef = useRef<{ past: Screen[]; future: Screen[] }>({ past: [], future: [] });
  const stageRef = useRef<HTMLDivElement>(null);
  const liveScreenRef = useRef(screen);
  liveScreenRef.current = screen;
  const clipboardRef = useRef<Control | null>(null);
  const selected = screen.controls.find(c => c.id === selectedId) ?? null;
  const variablesById: VariableMap = useMemo(() => Object.fromEntries(variables.map(v => [v.varId, v])), [variables]);
  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [s, v] = await Promise.all([fetch(`${API}/api/screens/main`), fetch(`${API}/api/variables`)]);
      if (!s.ok || !v.ok) throw new Error("本机运行时不可用，请先启动 HMIStudio.Server。");
      const [loadedScreen, loadedVariables] = await Promise.all([s.json(), v.json()]);
      const loaded = { ...loadedScreen, controls: loadedScreen.controls ?? [] } as Screen;
      liveScreenRef.current = loaded; setScreen(loaded); historyRef.current = { past: [], future: [] }; setHistoryVersion(v => v + 1);
      setVariables(loadedVariables);
      setSelectedId(null); setDirty(false); setMessage("");
    } catch (e) { setMessage(e instanceof Error ? e.message : "工程加载失败"); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);
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
    setSelectedId(control.id);
  };
  const removeSelected = () => {
    if (!selected) return;
    commitScreen({ ...screen, controls: screen.controls.filter(c => c.id !== selected.id) });
    setSelectedId(null);
  };
  const save = async () => {
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
  };
  const redo = () => {
    const next = historyRef.current.future.pop();
    if (!next) return;
    historyRef.current.past.push(JSON.parse(JSON.stringify(liveScreenRef.current)) as Screen);
    liveScreenRef.current = next; setScreen(next); setDirty(true); setHistoryVersion(v => v + 1);
    setSelectedId(id => next.controls.some(item => item.id === id) ? id : null);
  };
  const copySelected = () => { if (selected) clipboardRef.current = JSON.parse(JSON.stringify(selected)) as Control; };
  const pasteControl = () => {
    const source = clipboardRef.current; if (!source) return;
    const clone = JSON.parse(JSON.stringify(source)) as Control;
    clone.id = `${clone.controlType.toLowerCase()}-${Date.now().toString(36)}-${screen.controls.length}`;
    clone.name = `${source.name} 副本`; clone.x = Math.min(1280 - clone.width, source.x + 24); clone.y = Math.min(720 - clone.height, source.y + 24);
    commitScreen({ ...screen, controls: [...screen.controls, clone] }); setSelectedId(clone.id);
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
      else if ((event.key === "Delete" || event.key === "Backspace") && selected) { event.preventDefault(); removeSelected(); }
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
      <button className="editor-back" onClick={onBack}><ArrowLeft size={16} />返回运行监控</button>
      <div className="editor-project"><strong>HMIStudio</strong><span>/ 画面组态编辑器</span><i className={dirty ? "dirty-dot" : ""} />{dirty ? "未保存" : "已同步"}</div>
      <div className="editor-actions"><button className="editor-ghost" disabled={!historyRef.current.past.length} onClick={undo} title="撤销 Ctrl+Z">↶</button><button className="editor-ghost" disabled={!historyRef.current.future.length} onClick={redo} title="重做 Ctrl+Y">↷</button><button className="editor-ghost" disabled={!selected} onClick={copySelected} title="复制 Ctrl+C">复制</button><button className="editor-ghost" disabled={!clipboardRef.current} onClick={pasteControl} title="粘贴 Ctrl+V">粘贴</button><button className="editor-ghost" onClick={() => setZoom(z => Math.max(.5, +(z - .1).toFixed(1)))} title="缩小"><ZoomOut size={15} /></button><span>{Math.round(zoom * 100)}%</span><button className="editor-ghost" onClick={() => setZoom(z => Math.min(1.5, +(z + .1).toFixed(1)))} title="放大"><ZoomIn size={15} /></button><button className="editor-save" disabled={saving || loading || !dirty} onClick={() => void save()}><Save size={15} />{saving ? "保存中" : "保存工程"}</button></div>
    </header>
    <div className="editor-workspace">
      <aside className="editor-library">
        <div className="editor-pane-title"><Box size={15} />控件库</div>
        <p>选择控件添加到画布</p>
        <div className="editor-library-grid">{LIBRARY.map(item => <button key={item.type} className="editor-library-item" onClick={() => addControl(item.type)}><item.icon size={20} /><span>{item.label}</span><Plus size={13} /></button>)}</div>
        <div className="editor-library-note"><Grid2X2 size={15} /><span>画布基准尺寸<br /><b>1280 × 720</b></span></div>
      </aside>
      <main className="editor-center">
        <div className="editor-canvas-toolbar"><div><span className="editor-screen-dot" />{screen.title || "未命名画面"}<small>/{screen.id}</small></div><div><Eye size={14} />设计预览</div></div>
        <div className="editor-canvas-scroll">
          <div className="editor-stage-wrap" style={{ width: `${zoom * 100}%` }}>
            <div ref={stageRef} className="editor-stage" style={stageStyle} onPointerDown={e => { if (e.target === e.currentTarget) setSelectedId(null); }}>
              <div className="editor-grid" />
              {screen.controls.map(control => <ControlRenderer key={control.id} control={control} variables={variablesById} selected={selectedId === control.id} interactive onPointerDown={event => {
                event.stopPropagation();
                setSelectedId(control.id);
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
        <footer className="editor-statusbar"><span><Circle size={8} />{screen.controls.length} 个控件</span><span>吸附网格：8 px</span><span>{dirty ? "有未保存修改" : "所有修改已保存"}</span></footer>
      </main>
      <aside className="editor-inspector">
        <div className="editor-pane-title">属性检查器</div>
        {!selected ? <div className="inspector-empty"><MousePointer2 size={22} /><strong>未选择控件</strong><span>在画布中选择一个控件<br />即可编辑其属性</span></div> : <>
          <div className="inspector-control-title"><span>{selected.controlType}</span><small>{selected.id}</small></div>
          <label className="editor-field"><span>控件名称</span><input value={selected.name} onChange={e => patchControl(selected.id, { name: e.target.value })} /></label>
          <div className="editor-field-grid">
            {(["x", "y", "width", "height"] as const).map(key => <label className="editor-field" key={key}><span>{{ x: "X 坐标", y: "Y 坐标", width: "宽度", height: "高度" }[key]}</span><input type="number" min={key === "width" || key === "height" ? 8 : 0} value={selected[key]} onChange={e => patchControl(selected.id, { [key]: Math.max(key === "width" || key === "height" ? 8 : 0, Number(e.target.value) || 0) })} /></label>)}
          </div>
          {getControlDefinition(selected.controlType).properties.map(property => {
            if (property.key === "text") {
              return <label className="editor-field" key={property.key}><span>{property.label}</span><input value={textValue} onChange={e => patchProperty("text", e.target.value)} /></label>;
            }
            const value = String(staticValue(selected, property.key, property.defaultValue));
            if (property.kind === "color") {
              const color = /^#[0-9a-f]{6}$/i.test(value) ? value : String(property.defaultValue);
              return <label className="editor-field" key={property.key}><span>{property.label}</span><div className="editor-color-row"><input type="color" value={color} onChange={e => patchProperty(property.key, e.target.value)} /><input value={value} onChange={e => patchProperty(property.key, e.target.value)} /></div></label>;
            }
            return <label className="editor-field" key={property.key}><span>{property.label}</span><input type="number" min={property.min} max={property.max} step={property.step ?? 1} value={Number(resolveControlProperty(selected, property.key, variablesById, property.defaultValue))} onChange={e => patchProperty(property.key, Math.max(property.min ?? Number.MIN_SAFE_INTEGER, Math.min(property.max ?? Number.MAX_SAFE_INTEGER, Number(e.target.value) || Number(property.defaultValue))))} /></label>;
          })}
          {getControlDefinition(selected.controlType).properties.some(property => property.key === "text") && <>
            <label className="editor-field"><span>文本来源</span><select value={bindingMode} onChange={e => {
              if (e.target.value === "variable") patchControl(selected.id, { properties: { ...selected.properties, text: { source: { type: "$var", varId: variables[0]?.varId ?? "" }, direction: "OneWay" } } });
              else patchProperty("text", textValue);
            }}><option value="static">静态值</option><option value="variable">绑定变量</option></select></label>
            {bindingMode === "variable" && <label className="editor-field"><span>绑定变量</span><select value={String(binding?.type === "$var" ? binding.varId : "")} onChange={e => patchControl(selected.id, { properties: { ...selected.properties, text: { source: { type: "$var", varId: e.target.value }, direction: "OneWay" } } })}>{variables.map(v => <option key={v.varId} value={v.varId}>{v.name} · {v.varId}</option>)}</select></label>}
          </>}
          <div className="inspector-hint"><VariableIcon size={14} /><span>变量绑定已写入工程模型。运行时动态求值与类型校验将在后续阶段接入。</span></div>
          <button className="editor-delete" onClick={removeSelected}>删除此控件</button>
        </>}
      </aside>
    </div>
    {message && <div className={`editor-toast ${message.includes("失败") || message.includes("不可用") ? "error" : ""} `} role="status">{message}<button onClick={() => setMessage("")}>关闭</button></div>}
  </div>;
}
function colorValue(control: Control, key: string, fallback: string) { return String(staticValue(control, key, fallback)); }
