import type { KeyboardEvent } from "react";

const nonBlockingInputTypes = new Set([
  "button",
  "checkbox",
  "color",
  "file",
  "hidden",
  "image",
  "radio",
  "range",
  "reset",
  "submit",
]);

export const preventImplicitFormSubmit = (event: KeyboardEvent<HTMLFormElement>) => {
  const isComposing =
    "isComposing" in event.nativeEvent &&
    Boolean((event.nativeEvent as globalThis.KeyboardEvent).isComposing);

  if (event.key !== "Enter" || event.defaultPrevented || isComposing) {
    return;
  }

  const target = event.target;
  if (target instanceof HTMLTextAreaElement || target instanceof HTMLButtonElement) {
    return;
  }

  if (target instanceof HTMLInputElement && nonBlockingInputTypes.has(target.type)) {
    return;
  }

  event.preventDefault();
};
