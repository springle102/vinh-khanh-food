import { forwardRef, type InputHTMLAttributes, type TextareaHTMLAttributes } from "react";
import { cn } from "../../lib/utils";

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(
  ({ autoComplete, className, type, ...props }, ref) => (
    <input
      {...props}
      ref={ref}
      type={type}
      autoComplete={autoComplete ?? (type === "password" ? "new-password" : "off")}
      className={cn("field-input", className)}
    />
  ),
);

Input.displayName = "Input";

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>(
  ({ autoComplete, className, ...props }, ref) => (
    <textarea
      {...props}
      ref={ref}
      autoComplete={autoComplete ?? "off"}
      className={cn("field-input min-h-[120px] resize-y", className)}
    />
  ),
);

Textarea.displayName = "Textarea";
