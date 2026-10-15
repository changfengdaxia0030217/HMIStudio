import { useCallback, useEffect, useMemo, useState } from "react";
import { ArrowLeft, Box, Circle, CircleDot, Eye, Grid2X2, Lamp, MousePointer2, Plus, RectangleHorizontal, Save, Type, Variable as VariableIcon, ZoomIn, ZoomOut } from "lucide-react";
import "./editor.css";

const API = (import.meta.env.VITE_API_BASE as string | undefined) ?? "http://127.0.0.1:5080";
type Source = { type: "$static"; value?: unknown } | { type: "$var"; varId: string; index?: number } | { type: string; [key: string]: unknown };
type Binding = { source?: Source | null; direction?: "OneWay" | "TwoWay"; dependencies?: string[]; mapping?: unknown[] };
type Control = { id: string; name: string; controlType: string; x: number; y: number; width: number; height: number; properties: Record<string, Binding>; eventActions?: Record<string, unknown[]> };
type Screen = { id: string; title: string; background: string; screenType: string; width: number; height: number; controls: Control[] };
type Variable = { varId: string; name: string; dataType: string; value: unknown; writable: boolean };
const LIBRARY = [
  { type: "Text", label: "文本", icon: Type, w: 180, h: 42 },
  { type: "Button", label: "按钮", icon: MousePointer2, w: 140, h: 44 },
  { type: "Lamp", label: "指示灯", icon: Lamp, w: 72, h: 72 },
  { type: "NumericDisplay", label: "数值显示", icon: VariableIcon, w: 160, h: 48 },
  { type: "NumericInput", label: "数值输入", icon: CircleDot, w: 160, h: 44 },
  { type: "Rectangle", label: "矩形", icon: RectangleHorizontal, w: 180, h: 100 },
] as const;
const fallbackScreen: Screen = { id: "main", title: "设备总览", background: "#111827", screenType: "Normal", width: 1280, height: 720, controls: [] };
const staticValue = (control: Control, key: string, fallback: unknown) => {
  const source = control.properties?.[key]?.source;
  if (source?.type === "$static") return source.value ?? fallback;
  if (source?.type === "$var") return source.varId;
  return fallback;
};
function newControl(type: string, index: number): Control {
  const item = LIBRARY.find(x => x.type === type) ?? LIBRARY[0];
  const props: Record<string, Binding> = {};
  if (type === "Text") {
    props.text = { source: { type: "$static", value: "新建文本" } };
    props.color = { source: { type: "$static", value: "#E5EAF3" } };
    props.fontSize = { source: { type: "$static", value: 24 } };
  } else if (type === "Button") {
    props.text = { source: { type: "$static", value: "按钮" } };
    props.color = { source: { type: "$static", value: "#FFFFFF" } };
    props.background = { source: { type: "$static", value: "#2563EB" } };
  } else if (type === "Lamp") {
    props.color = { source: { type: "$static", value: "#38D9A9" } };
  } else if (type === "NumericDisplay" || type === "NumericInput") {
    props.text = { source: { type: "$static", value: "42.5" } };
    props.color = { source: { type: "$static", value: "#38D9A9" } };
    props.fontSize = { source: { type: "$static", value: 28 } };
  } else {
    props.color = { source: { type: "$static", value: "#334155" } };
    props.background = { source: { type: "$static", value: "#1E293B" } };
  }
  return { id: `${type.toLowerCase()}-${Date.now().toString(36)}-${index}`, name: item.label, controlType: type, x: 100 + index * 18, y: 100 + index * 18, width: item.w, height: item.h, properties: props, eventActions: {} };
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
  const selected = screen.controls.find(c => c.id === selectedId) ?? null;
  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [s, v] = await Promise.all([fetch(`${API}/api/screens/main`), fetch(`${API}/api/variables`)]);
      if (!s.ok || !v.ok) throw new Error("本机运行时不可用，请先启动 HMIStudio.Server。");
      const [loadedScreen, loadedVariables] = await Promise.all([s.json(), v.json()]);
      setScreen({ ...loadedScreen, controls: loadedScreen.controls ?? [] });
      setVariables(loadedVariables);
      setSelectedId(null); setDirty(false); setMessage("");
    } catch (e) { setMessage(e instanceof Error ? e.message : "工程加载失败"); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);
  const patchControl = (id: string, patch: Partial<Control>) => {
    setScreen(previous => ({ ...previous, controls: previous.controls.map(c => c.id === id ? { ...c, ...patch } : c) }));
    setDirty(true);
  };
  const patchProperty = (key: string, value: unknown) => {
    if (!selected) return;
    const properties = { ...selected.properties, [key]: { ...(selected.properties[key] ?? {}), source: { type: "$static", value } } as Binding };
    patchControl(selected.id, { properties });
  };
  const addControl = (type: string) => {
    const control = newControl(type, screen.controls.length);
    setScreen(previous => ({ ...previous, controls: [...previous.controls, control] }));
    setSelectedId(control.id); setDirty(true);
  };
  const removeSelected = () => {
    if (!selected) return;
    setScreen(previous => ({ ...previous, controls: previous.controls.filter(c => c.id !== selected.id) }));
    setSelectedId(null); setDirty(true);
  };
  const save = async () => {
    setSaving(true); setMessage("");
    try {
      const response = await fetch(`${API}/api/screens/${encodeURIComponent(screen.id)}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(screen) });
      const result = await response.json();
      if (!response.ok) throw new Error(result.error ?? "保存失败");
      setDirty(false); setMessage("工程已保存到本机 JSON 文件。");
    } catch (e) { setMessage(e instanceof Error ? e.message : "保存失败"); }
    finally { setSaving(false); }
  };
  const binding = selected?.properties?.text?.source;
  const bindingMode = binding?.type === "$var" ? "variable" : "static";
  const textValue = String(staticValue(selected ?? newControl("Text", 0), "text", selected?.name ?? ""));
  const stageStyle = useMemo(() => ({ background: screen.background || "#111827" }), [screen.background]);
  return <div className="editor-shell">
    <header className="editor-topbar">
      <button className="editor-back" onClick={onBack}><ArrowLeft size={16} />返回运行监控</button>
      <div className="editor-project"><strong>HMIStudio</strong><span>/ 画面组态编辑器</span><i className={dirty ? "dirty-dot" : ""} />{dirty ? "未保存" : "已同步"}</div>
      <div className="editor-actions"><button className="editor-ghost" onClick={() => setZoom(z => Math.max(.5, +(z - .1).toFixed(1)))} title="缩小"><ZoomOut size={15} /></button><span>{Math.round(zoom * 100)}%</span><button className="editor-ghost" onClick={() => setZoom(z => Math.min(1.5, +(z + .1).toFixed(1)))} title="放大"><ZoomIn size={15} /></button><button className="editor-save" disabled={saving || loading || !dirty} onClick={() => void save()}><Save size={15} />{saving ? "保存中" : "保存工程"}</button></div>
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
            <div className="editor-stage" style={stageStyle} onPointerDown={e => { if (e.target === e.currentTarget) setSelectedId(null); }}>
              <div className="editor-grid" />
              {screen.controls.map(control => {
                const label = String(staticValue(control, "text", control.name || control.controlType));
                const color = String(staticValue(control, "color", "#E5EAF3"));
                const background = String(staticValue(control, "background", "transparent"));
                const fontSize = Number(staticValue(control, "fontSize", 18)) || 18;
                return <div key={control.id} className={`editor-control ${selectedId === control.id ? "selected" : ""} control-${control.controlType.toLowerCase()}`} style={{ left: `${control.x / 12.8}%`, top: `${control.y / 7.2}%`, width: `${control.width / 12.8}%`, height: `${control.height / 7.2}%`, color, background, fontSize }} onPointerDown={event => {
                  event.stopPropagation(); setSelectedId(control.id);
                  const stage = event.currentTarget.parentElement;
                  if (!stage) return;
                  const rect = stage.getBoundingClientRect();
                  const startX = event.clientX, startY = event.clientY, originX = control.x, originY = control.y;
                  (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
                  const move = (pointer: PointerEvent) => {
                    const dx = (pointer.clientX - startX) / rect.width * 1280;
                    const dy = (pointer.clientY - startY) / rect.height * 720;
                    setScreen(previous => ({ ...previous, controls: previous.controls.map(c => c.id === control.id ? { ...c, x: Math.max(0, Math.min(1280 - c.width, Math.round((originX + dx) / 8) * 8)), y: Math.max(0, Math.min(720 - c.height, Math.round((originY + dy) / 8) * 8)) } : c) }));
                    setDirty(true);
                  };
                  const up = () => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); };
                  window.addEventListener("pointermove", move); window.addEventListener("pointerup", up, { once: true });
                }}>
                  {control.controlType === "Lamp" ? <span className="editor-lamp" style={{ background: color }} /> : control.controlType === "Rectangle" ? <span className="editor-rect-label">{control.name}</span> : label}
                  {selectedId === control.id && <span className="editor-control-handle" />}
                </div>;
              })}
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
          {selected.controlType !== "Rectangle" && selected.controlType !== "Lamp" && <label className="editor-field"><span>显示文本</span><input value={textValue} onChange={e => patchProperty("text", e.target.value)} /></label>}
          <label className="editor-field"><span>文本 / 指示色</span><div className="editor-color-row"><input type="color" value={/^#[0-9a-f]{6}$/i.test(colorValue(selected, "color", "#E5EAF3")) ? colorValue(selected, "color", "#E5EAF3") : "#E5EAF3"} onChange={e => patchProperty("color", e.target.value)} /><input value={colorValue(selected, "color", "#E5EAF3")} onChange={e => patchProperty("color", e.target.value)} /></div></label>
          <label className="editor-field"><span>文字大小</span><input type="number" min={8} max={96} value={Number(staticValue(selected, "fontSize", 18))} onChange={e => patchProperty("fontSize", Math.max(8, Math.min(96, Number(e.target.value) || 18)))} /></label>
          <label className="editor-field"><span>文本来源</span><select value={bindingMode} onChange={e => {
            if (e.target.value === "variable") patchControl(selected.id, { properties: { ...selected.properties, text: { source: { type: "$var", varId: variables[0]?.varId ?? "" }, direction: "OneWay" } } });
            else patchProperty("text", textValue);
          }}><option value="static">静态值</option><option value="variable">绑定变量</option></select></label>
          {bindingMode === "variable" && <label className="editor-field"><span>绑定变量</span><select value={String(binding?.type === "$var" ? binding.varId : "")} onChange={e => patchControl(selected.id, { properties: { ...selected.properties, text: { source: { type: "$var", varId: e.target.value }, direction: "OneWay" } } })}>{variables.map(v => <option key={v.varId} value={v.varId}>{v.name} · {v.varId}</option>)}</select></label>}
          <div className="inspector-hint"><VariableIcon size={14} /><span>变量绑定已写入工程模型。运行时动态求值与类型校验将在后续阶段接入。</span></div>
          <button className="editor-delete" onClick={removeSelected}>删除此控件</button>
        </>}
      </aside>
    </div>
    {message && <div className={`editor-toast ${message.includes("失败") || message.includes("不可用") ? "error" : ""} `} role="status">{message}<button onClick={() => setMessage("")}>关闭</button></div>}
  </div>;
}
function colorValue(control: Control, key: string, fallback: string) { return String(staticValue(control, key, fallback)); }
