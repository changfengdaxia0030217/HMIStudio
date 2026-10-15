import type { CSSProperties, PointerEvent as ReactPointerEvent } from "react";

export type BoundSource = { type?: string; value?: unknown; varId?: string; index?: number };
export type BoundProperty = { source?: BoundSource | null; direction?: string };
export type ScreenControl = {
  id: string; name: string; controlType: string; x: number; y: number; width: number; height: number;
  properties: Record<string, BoundProperty>;
};
export type RuntimeVariable = { varId: string; name?: string; value: unknown; quality?: string };
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
export function ControlRenderer({ control, variables, selected = false, interactive = false, onPointerDown, className = "" }: {
  control: ScreenControl; variables: VariableMap; selected?: boolean; interactive?: boolean;
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
  const style: CSSProperties = {
    left: `${control.x / 12.8}%`, top: `${control.y / 7.2}%`,
    width: `${control.width / 12.8}%`, height: `${control.height / 7.2}%`,
    color, background, fontSize, cursor: interactive ? "move" : "default",
  };
  return <div className={`hmi-control control-${control.controlType.toLowerCase()} ${selected ? "selected" : ""} ${className}`} style={style} onPointerDown={onPointerDown} data-control-id={control.id}>
    {control.controlType === "Lamp"
      ? <span className="hmi-lamp" style={{ background: color, boxShadow: `0 0 20px ${color}, inset 0 0 8px #fff5` }} />
      : control.controlType === "Rectangle"
        ? <span className="hmi-rect-label">{control.name}</span>
        : text}
    {selected && ["nw", "n", "ne", "e", "se", "s", "sw", "w"].map(position => <span key={position} className={`hmi-control-handle handle-${position}`} />)}
  </div>;
}
