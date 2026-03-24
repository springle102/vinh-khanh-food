import type { SVGProps } from "react";

type IconName =
  | "dashboard"
  | "map"
  | "content"
  | "audio"
  | "users"
  | "gift"
  | "star"
  | "chart"
  | "activity"
  | "settings"
  | "search"
  | "plus"
  | "logout"
  | "bell"
  | "play"
  | "pin"
  | "route"
  | "menu";

const paths: Record<IconName, string[]> = {
  dashboard: ["M4 13h7V4H4v9Zm0 7h7v-5H4v5Zm9 0h7V11h-7v9Zm0-16v5h7V4h-7Z"],
  map: [
    "m9 18 6-2 6 2V6l-6-2-6 2-6-2v12l6 2Z",
    "M9 4v14",
    "M15 6v12",
  ],
  content: ["M6 4h9l5 5v11a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z", "M14 4v6h6"],
  audio: ["M11 5 6 9H3v4h3l5 4V5Zm4.54 3.46a5 5 0 0 1 0 7.08", "M17.66 1.34a10 10 0 0 1 0 14.14"],
  users: ["M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2", "M8.5 11A4.5 4.5 0 1 0 8.5 2a4.5 4.5 0 0 0 0 9Z", "M20 8v6", "M23 11h-6"],
  gift: ["M20 12v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-8", "M2 7h20v5H2V7Zm10 0v15", "M10 7H6.5A2.5 2.5 0 1 1 9 4.5L12 7", "M14 7h3.5A2.5 2.5 0 1 0 15 4.5L12 7"],
  star: ["m12 17.27 6.18 3.73-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z"],
  chart: ["M3 3v18h18", "M7 14l4-4 3 3 5-7"],
  activity: ["M22 12h-4l-3 9-4-18-3 9H2"],
  settings: ["M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8Zm8.94 4a7.97 7.97 0 0 0-.34-2.3l2.03-1.58-2-3.46-2.39.96a8.15 8.15 0 0 0-3.98-2.3L13.5 1h-3l-.76 2.32a8.15 8.15 0 0 0-3.98 2.3l-2.39-.96-2 3.46 2.03 1.58a8.65 8.65 0 0 0 0 4.6l-2.03 1.58 2 3.46 2.39-.96a8.15 8.15 0 0 0 3.98 2.3L10.5 23h3l.76-2.32a8.15 8.15 0 0 0 3.98-2.3l2.39.96 2-3.46-2.03-1.58c.23-.74.34-1.5.34-2.3Z"],
  search: ["m21 21-4.35-4.35", "M10 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16Z"],
  plus: ["M12 5v14", "M5 12h14"],
  logout: ["M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4", "M16 17l5-5-5-5", "M21 12H9"],
  bell: ["M15 17h5l-1.4-1.4A2 2 0 0 1 18 14.2V11a6 6 0 1 0-12 0v3.2a2 2 0 0 1-.6 1.4L4 17h5", "M10 21h4"],
  play: ["M8 5v14l11-7-11-7Z"],
  pin: ["M12 21s7-4.35 7-11a7 7 0 1 0-14 0c0 6.65 7 11 7 11Z", "M12 11a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z"],
  route: ["M6 19a2 2 0 1 0 0-4 2 2 0 0 0 0 4Zm12-12a2 2 0 1 0 0-4 2 2 0 0 0 0 4ZM8 17h5a4 4 0 0 0 4-4V9", "M16 5h-3"],
  menu: ["M4 6h16", "M4 12h16", "M4 18h16"],
};

export const Icon = ({
  name,
  className,
  ...props
}: SVGProps<SVGSVGElement> & { name: IconName }) => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.8"
    strokeLinecap="round"
    strokeLinejoin="round"
    className={className}
    aria-hidden="true"
    {...props}
  >
    {paths[name].map((path) => (
      <path key={path} d={path} />
    ))}
  </svg>
);
