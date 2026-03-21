import { cn } from "../../lib/utils";

export interface TabItem {
  key: string;
  label: string;
  badge?: string;
}

export const Tabs = ({
  items,
  value,
  onChange,
}: {
  items: TabItem[];
  value: string;
  onChange: (nextValue: string) => void;
}) => (
  <div className="flex flex-wrap gap-2">
    {items.map((item) => (
      <button
        key={item.key}
        type="button"
        onClick={() => onChange(item.key)}
        className={cn(
          "inline-flex items-center gap-2 rounded-full border px-4 py-2 text-sm font-medium transition",
          value === item.key
            ? "border-primary-200 bg-primary-50 text-primary-700"
            : "border-sand-200 bg-white text-ink-600 hover:border-sand-300 hover:bg-sand-50",
        )}
      >
        <span>{item.label}</span>
        {item.badge ? (
          <span className="rounded-full bg-white/80 px-2 py-0.5 text-xs">{item.badge}</span>
        ) : null}
      </button>
    ))}
  </div>
);
