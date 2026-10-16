import { useEffect, useRef, useState } from "react";
import type { CSSProperties, PointerEvent as ReactPointerEvent } from "react";

export type BoundSource = { type?: string; value?: unknown; varId?: string; index?: number };
export type BoundProperty = { source?: BoundSource | null; direction?: string };
export type ScreenControl = {
  id: string; name: string; controlType: string; x: number; y: number; width: number; height: number;
  properties: Record<string, BoundProperty>;
};
export type RuntimeVariable = { varId: string; name?: string; value: unknown; quality?: string; writable?: boolean; min?: number | null; max?: number | null; dataType?: string };
export type VariableMap = Record<string, RuntimeVariable | undefined>;

export function resolveControlProperty(control: ScreenControl, key: string, variables: VariableMap, fallback: unknown): unknown {
  const source = control.properties?.[key]?.source;
  if (!source) return fallback;
  if (source.type === "$static") return source.value ?? fallback;
  if (source.type === "$var") return variables[String(source.varId ?? "")]?.value ?? fallback;
  return fallback;
}
function printable(value: unknown): string {
  if (value === null || value === undefined) return "—";
  if (typeof value === "boolean") return value ? "TRUE" : "FALSE";
  if (typeof value === "number") return Number.isInteger(value) ? String(value) : value.toFixed(1);
  return String(value);
}
function parseWriteValue(raw: unknown, current: unknown): unknown {
  if (typeof raw === "string" && raw.toLowerCase() === "toggle")
    return typeof current === "boolean" ? !current : current;
  if (typeof current === "boolean" && typeof raw === "string") {
    if (raw.toLowerCase() === "true") return true;
    if (raw.toLowerCase() === "false") return false;
  }
  if (typeof current === "number" && String(raw).trim() !== "" && Number.isFinite(Number(raw))) return Number(raw);
  return raw;
}
export function ControlRenderer({ control, variables, selected = false, interactive = false, runtime = false, onWrite, onNavigate, onPointerDown, className = "" }: {
  control: ScreenControl; variables: VariableMap; selected?: boolean; interactive?: boolean; runtime?: boolean;
  onWrite?: (varId: string, value: unknown) => void;
  onNavigate?: (screenId: string) => void;
  onPointerDown?: (event: ReactPointerEvent<HTMLDivElement>) => void; className?: string;
}) {
  const text = printable(resolveControlProperty(control, "text", variables, control.name || control.controlType));
  const rawColor = resolveControlProperty(control, "color", variables, "#E5EAF3");
  const colorSource = control.properties?.color?.source;
  const colorVariable = colorSource?.type === "$var" ? variables[String(colorSource.varId ?? "")] : undefined;
  const color = colorVariable && typeof colorVariable.value === "boolean"
    ? (colorVariable.value ? "#38D9A9" : "#F87171")
    : String(rawColor ?? "#E5EAF3");
  const background = String(resolveControlProperty(control, "background", variables, "transparent") ?? "transparent");
  const fontSize = Number(resolveControlProperty(control, "fontSize", variables, 18)) || 18;
  const inputSource = control.properties?.text?.source;
  const inputVarId = inputSource?.type === "$var" ? String(inputSource.varId ?? "") : "";
  const inputVariable = inputVarId ? variables[inputVarId] : undefined;
  const [draft, setDraft] = useState(String(inputVariable?.value ?? text));
  const editing = useRef(false);
  const [inputError, setInputError] = useState("");
  useEffect(() => {
    if (!editing.current) setDraft(String(inputVariable?.value ?? text));
  }, [inputVariable?.value, text]);
  const targetVarId = String(resolveControlProperty(control, "writeVarId", variables, "") ?? "");
  const writeValue = resolveControlProperty(control, "writeValue", variables, "toggle");
  const navigateToScreenId = String(resolveControlProperty(control, "navigateToScreenId", variables, "") ?? "").trim();
  const writeVariable = targetVarId ? variables[targetVarId] : undefined;
  const writeQualityBad = Boolean(writeVariable?.quality && writeVariable.quality !== "Good");
  const buttonWriteBlocked = runtime && control.controlType === "Button" && !navigateToScreenId && Boolean(targetVarId) && (!writeVariable?.writable || writeQualityBad);
  const inputQualityBad = Boolean(inputVariable?.quality && inputVariable.quality !== "Good");
  const boundVariableIds = [...new Set(Object.values(control.properties ?? {})
    .map(property => property?.source)
    .filter((source): source is BoundSource => source?.type === "$var")
    .map(source => String(source.varId ?? ""))
    .filter(Boolean))];
  const degradedVariable = boundVariableIds
    .map(varId => variables[varId] ?? ({ varId, value: undefined, quality: "BadNotExist" } as RuntimeVariable))
    .find(variable => variable.quality !== "Good");
  const qualityLabel = degradedVariable?.quality === "BadComm" ? "通讯异常"
    : degradedVariable?.quality === "BadNotExist" ? "变量不存在"
      : degradedVariable?.quality === "Uncertain" ? "质量不确定"
        : degradedVariable ? "质量未知或异常" : "";
  const style: CSSProperties = {
    left: `${control.x / 12.8}%`, top: `${control.y / 7.2}%`,
    width: `${control.width / 12.8}%`, height: `${control.height / 7.2}%`,
    color, background, fontSize, cursor: interactive ? "move" : runtime && control.controlType === "Button" ? "pointer" : "default",
  };
  return <div className={`hmi-control control-${control.controlType.toLowerCase()} ${selected ? "selected" : ""} ${degradedVariable ? "quality-degraded" : ""} ${className}`} style={style} onPointerDown={onPointerDown} data-control-id={control.id} data-quality-label={qualityLabel} title={qualityLabel || undefined}>
    {runtime && control.controlType === "Button"
      ? <button type="button" className="hmi-runtime-button" disabled={buttonWriteBlocked} title={buttonWriteBlocked ? (writeQualityBad ? "变量质量异常，已禁用写入" : "目标变量不可写，已禁用操作") : undefined} onPointerDown={event => event.stopPropagation()} onClick={() => {
          if (navigateToScreenId) {
            onNavigate?.(navigateToScreenId);
            return;
          }
          if (!targetVarId || !onWrite) return;
          const current = variables[targetVarId]?.value;
          onWrite(targetVarId, parseWriteValue(writeValue, current));
        }}>{text}</button>
      : runtime && control.controlType === "NumericInput"
        ? <input className="hmi-runtime-number" type="number" value={draft} min={inputVariable?.min ?? undefined} max={inputVariable?.max ?? undefined} step={inputVariable?.dataType?.toLowerCase() === "int" || inputVariable?.dataType?.toLowerCase() === "dint" || inputVariable?.dataType?.toLowerCase() === "byte" ? 1 : "any"} aria-invalid={Boolean(inputError)} disabled={!inputVariable?.writable || !onWrite || inputQualityBad} title={inputQualityBad ? (inputVariable?.quality === "BadComm" ? "通讯异常，已禁用写入" : inputVariable?.quality === "Uncertain" ? "变量质量不确定，已禁用写入" : "变量不可用，已禁用写入") : inputError || undefined}
            onPointerDown={event => event.stopPropagation()}
            onFocus={() => { editing.current = true; }}
            onChange={event => { setDraft(event.target.value); setInputError(""); }}
            onBlur={() => {
              editing.current = false;
              const value = Number(draft);
              if (!inputVarId || !inputVariable?.writable || !onWrite || inputQualityBad || draft.trim() === "" || !Number.isFinite(value)) {
                setDraft(String(inputVariable?.value ?? text));
                return;
              }
              const dataType = String(inputVariable.dataType ?? "").toLowerCase();
              const integerType = ["byte", "int", "dint"].includes(dataType);
              if (integerType && !Number.isInteger(value)) {
                setInputError("该变量只接受整数");
                setDraft(String(inputVariable.value ?? text));
                return;
              }
              const typeMin = dataType === "byte" ? 0 : dataType === "int" ? -32768 : dataType === "dint" ? -2147483648 : dataType === "float" ? -3.4028234663852886e38 : Number.NEGATIVE_INFINITY;
              const typeMax = dataType === "byte" ? 255 : dataType === "int" ? 32767 : dataType === "dint" ? 2147483647 : dataType === "float" ? 3.4028234663852886e38 : Number.POSITIVE_INFINITY;
              if (value < typeMin || value > typeMax) {
                setInputError(`该变量的数据类型 ${dataType} 不允许此数值`);
                setDraft(String(inputVariable.value ?? text));
                return;
              }
              if (inputVariable.min != null && value < inputVariable.min) {
                setInputError(`最小允许值为 ${inputVariable.min}`);
                setDraft(String(inputVariable.value ?? text));
                return;
              }
              if (inputVariable.max != null && value > inputVariable.max) {
                setInputError(`最大允许值为 ${inputVariable.max}`);
                setDraft(String(inputVariable.value ?? text));
                return;
              }
              setInputError("");
              onWrite(inputVarId, value);
            }} />
      : control.controlType === "Lamp"
        ? <span className="hmi-lamp" style={{ background: color, boxShadow: `0 0 20px ${color}, inset 0 0 8px #fff5` }} />
        : control.controlType === "Rectangle"
          ? <span className="hmi-rect-label">{control.name}</span>
          : text}
    {inputError && runtime && control.controlType === "NumericInput" && <span className="hmi-runtime-input-error" role="alert">{inputError}</span>}
    {selected && ["nw", "n", "ne", "e", "se", "s", "sw", "w"].map(position => <span key={position} className={`hmi-control-handle handle-${position}`} />)}
  </div>;
}
