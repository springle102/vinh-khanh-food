import { useId, useState, type ChangeEvent } from "react";
import { cn } from "../../lib/utils";
import { Input } from "./Input";

type SourceMode = "device" | "url";

type ImageSourceFieldProps = {
  label: string;
  value: string;
  onChange: (value: string) => void;
  onUpload: (file: File) => Promise<string>;
  urlPlaceholder?: string;
};

const getInitialMode = (value: string): SourceMode => (value ? "url" : "device");

export const ImageSourceField = ({
  label,
  value,
  onChange,
  onUpload,
  urlPlaceholder = "https://...",
}: ImageSourceFieldProps) => {
  const inputId = useId();
  const [mode, setMode] = useState<SourceMode>(() => getInitialMode(value));
  const [isUploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState("");

  const handleFileChange = async (event: ChangeEvent<HTMLInputElement>) => {
    const nextFile = event.target.files?.[0];
    event.target.value = "";

    if (!nextFile) {
      return;
    }

    setUploading(true);
    setUploadError("");

    try {
      const nextValue = await onUpload(nextFile);
      onChange(nextValue);
    } catch (error) {
      setUploadError(error instanceof Error ? error.message : "Không thể upload ảnh.");
    } finally {
      setUploading(false);
    }
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <label className="field-label mb-0" htmlFor={inputId}>
          {label}
        </label>
        <div className="inline-flex rounded-2xl bg-sand-50 p-1">
          {[
            ["device", "Từ thiết bị"],
            ["url", "Dùng URL"],
          ].map(([nextMode, nextLabel]) => (
            <button
              key={nextMode}
              type="button"
              onClick={() => {
                setMode(nextMode as SourceMode);
                setUploadError("");
              }}
              className={cn(
                "rounded-2xl px-3 py-2 text-xs font-semibold transition",
                mode === nextMode
                  ? "bg-white text-primary-700 shadow-soft"
                  : "text-ink-500 hover:text-ink-800",
              )}
            >
              {nextLabel}
            </button>
          ))}
        </div>
      </div>

      {mode === "url" ? (
        <Input
          id={inputId}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={urlPlaceholder}
        />
      ) : (
        <div className="space-y-2">
          <input
            id={inputId}
            type="file"
            accept="image/*"
            onChange={(event) => {
              void handleFileChange(event);
            }}
            className="field-input file:mr-4 file:rounded-2xl file:border-0 file:bg-primary-50 file:px-4 file:py-2 file:font-semibold file:text-primary-700"
          />
          <p className="text-xs text-ink-500">
            Ảnh từ thiết bị sẽ được upload lên backend storage trước khi lưu dữ liệu.
          </p>
          {isUploading ? (
            <div className="rounded-2xl bg-sand-50 px-4 py-3 text-sm text-ink-600">
              Đang upload ảnh...
            </div>
          ) : null}
          {uploadError ? (
            <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">
              {uploadError}
            </div>
          ) : null}
        </div>
      )}

      {value ? (
        <div className="overflow-hidden rounded-3xl border border-sand-200 bg-sand-50">
          <img src={value} alt={label} className="h-48 w-full object-cover" />
        </div>
      ) : null}
    </div>
  );
};
