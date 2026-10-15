export type PropertyEditorKind = "text" | "color" | "number";

export type ControlPropertyDefinition = {
  key: string;
  label: string;
  kind: PropertyEditorKind;
  defaultValue: string | number;
  min?: number;
  max?: number;
  step?: number;
};

export type ControlDefinition = {
  type: string;
  label: string;
  width: number;
  height: number;
  properties: ControlPropertyDefinition[];
};

const commonColor: ControlPropertyDefinition = {
  key: "color", label: "前景色", kind: "color", defaultValue: "#E5EAF3",
};
const commonText: ControlPropertyDefinition = {
  key: "text", label: "显示文本", kind: "text", defaultValue: "",
};
const commonFontSize: ControlPropertyDefinition = {
  key: "fontSize", label: "文字大小", kind: "number", defaultValue: 18, min: 8, max: 96, step: 1,
};
const background: ControlPropertyDefinition = {
  key: "background", label: "背景色", kind: "color", defaultValue: "#1E293B",
};

export const CONTROL_REGISTRY: ControlDefinition[] = [
  { type: "Text", label: "文本", width: 180, height: 42, properties: [
    { ...commonText, defaultValue: "新建文本" }, commonColor, { ...commonFontSize, defaultValue: 24 },
  ] },
  { type: "Button", label: "按钮", width: 140, height: 44, properties: [
    { ...commonText, defaultValue: "按钮" }, { ...commonColor, defaultValue: "#FFFFFF" },
    { ...background, defaultValue: "#2563EB" },
  ] },
  { type: "Lamp", label: "指示灯", width: 72, height: 72, properties: [
    { ...commonColor, defaultValue: "#38D9A9" },
  ] },
  { type: "NumericDisplay", label: "数值显示", width: 160, height: 48, properties: [
    { ...commonText, defaultValue: "42.5" }, { ...commonColor, defaultValue: "#38D9A9" },
    { ...commonFontSize, defaultValue: 28 },
  ] },
  { type: "NumericInput", label: "数值输入", width: 160, height: 44, properties: [
    { ...commonText, defaultValue: "42.5" }, { ...commonColor, defaultValue: "#38D9A9" },
    { ...commonFontSize, defaultValue: 28 },
  ] },
  { type: "Rectangle", label: "矩形", width: 180, height: 100, properties: [
    { ...commonColor, defaultValue: "#334155" }, { ...background, defaultValue: "#1E293B" },
  ] },
];

export function getControlDefinition(type: string): ControlDefinition {
  return CONTROL_REGISTRY.find(item => item.type === type) ?? CONTROL_REGISTRY[0];
}
