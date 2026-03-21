import type { Config } from "tailwindcss";

export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        primary: {
          50: "#fff7ed",
          100: "#ffedd5",
          200: "#fed7aa",
          300: "#fdba74",
          400: "#fb923c",
          500: "#f97316",
          600: "#ea580c",
          700: "#c2410c",
          800: "#9a3412",
          900: "#7c2d12",
        },
        clay: {
          50: "#fff8f6",
          100: "#fde9e3",
          200: "#f8cec2",
          300: "#f2ad98",
          400: "#ea8368",
          500: "#de6245",
          600: "#c94c31",
          700: "#a73b28",
          800: "#873225",
          900: "#702d23",
        },
        sand: {
          50: "#fffef9",
          100: "#faf4e7",
          200: "#f4e5c4",
          300: "#ecd196",
          400: "#e5be67",
          500: "#d9a845",
          600: "#bf8534",
          700: "#99642b",
          800: "#7d4f29",
          900: "#684225",
        },
        ink: {
          50: "#f7f7f8",
          100: "#eeeef0",
          200: "#d9d9de",
          300: "#b7b8c0",
          400: "#8f91a0",
          500: "#6e7181",
          600: "#585a69",
          700: "#474955",
          800: "#3b3c46",
          900: "#26272f",
        },
      },
      fontFamily: {
        sans: ["\"Plus Jakarta Sans\"", "\"Segoe UI\"", "\"Helvetica Neue\"", "sans-serif"],
      },
      boxShadow: {
        soft: "0 18px 45px -24px rgba(124, 45, 18, 0.25)",
      },
      backgroundImage: {
        "hero-warm":
          "radial-gradient(circle at top left, rgba(249, 115, 22, 0.22), transparent 34%), radial-gradient(circle at bottom right, rgba(217, 168, 69, 0.16), transparent 28%)",
      },
    },
  },
  plugins: [],
} satisfies Config;
