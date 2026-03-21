import type { ButtonHTMLAttributes, PropsWithChildren } from "react";
import { cn } from "../../lib/utils";

type ButtonProps = PropsWithChildren<
  ButtonHTMLAttributes<HTMLButtonElement> & {
    variant?: "primary" | "secondary" | "ghost" | "danger";
  }
>;

const variants = {
  primary:
    "bg-primary-500 text-white shadow-soft hover:bg-primary-600 focus-visible:ring-primary-200",
  secondary:
    "bg-sand-100 text-ink-900 hover:bg-sand-200 focus-visible:ring-sand-200",
  ghost:
    "bg-transparent text-ink-700 hover:bg-sand-100 focus-visible:ring-sand-200",
  danger:
    "bg-clay-500 text-white shadow-soft hover:bg-clay-600 focus-visible:ring-clay-200",
};

export const Button = ({
  children,
  className,
  type = "button",
  variant = "primary",
  ...props
}: ButtonProps) => (
  <button
    type={type}
    className={cn(
      "inline-flex items-center justify-center gap-2 rounded-2xl px-4 py-2.5 text-sm font-semibold transition focus-visible:outline-none focus-visible:ring-4 disabled:cursor-not-allowed disabled:opacity-60",
      variants[variant],
      className,
    )}
    {...props}
  >
    {children}
  </button>
);
