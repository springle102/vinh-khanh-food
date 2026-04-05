import { useId, useState, type ChangeEvent } from "react";

type ImageSourceFieldProps = {
  label: string;
  value: string;
  onChange: (value: string) => void;
  onUpload: (file: File) => Promise<string>;
  accept?: string;
  previewType?: "image" | "video";
  helperText?: string;
  emptyText?: string;
};

export const ImageSourceField = ({
  label,
  value,
  onChange,
  onUpload,
  accept = "image/*",
  previewType = "image",
  helperText = "Tệp từ thiết bị sẽ được upload lên backend storage trước khi lưu dữ liệu.",
  emptyText = "Chưa có tệp nào được tải lên.",
}: ImageSourceFieldProps) => {
  const inputId = useId();
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
      setUploadError(error instanceof Error ? error.message : "Không thể upload tệp.");
    } finally {
      setUploading(false);
    }
  };

  return (
    <div className="space-y-3">
      <label className="field-label" htmlFor={inputId}>
        {label}
      </label>

      <div className="space-y-2">
        <input
          id={inputId}
          type="file"
          accept={accept}
          onChange={(event) => {
            void handleFileChange(event);
          }}
          className="field-input file:mr-4 file:rounded-2xl file:border-0 file:bg-primary-50 file:px-4 file:py-2 file:font-semibold file:text-primary-700"
        />
        <p className="text-xs text-ink-500">{helperText}</p>
        {isUploading ? (
          <div className="rounded-2xl bg-sand-50 px-4 py-3 text-sm text-ink-600">
            Đang upload tệp...
          </div>
        ) : null}
        {uploadError ? (
          <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">
            {uploadError}
          </div>
        ) : null}
      </div>

      {value ? (
        <div className="space-y-3">
          <div className="overflow-hidden rounded-3xl border border-sand-200 bg-sand-50">
            {previewType === "video" ? (
              <video controls src={value} className="h-48 w-full bg-black object-contain" />
            ) : (
              <img src={value} alt={label} className="h-48 w-full object-cover" />
            )}
          </div>
          <p className="break-all text-xs text-ink-500">{value}</p>
        </div>
      ) : (
        <div className="rounded-2xl border border-dashed border-sand-200 bg-sand-50 px-4 py-3 text-sm text-ink-500">
          {emptyText}
        </div>
      )}
    </div>
  );
};
