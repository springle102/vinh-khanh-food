FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm install
COPY . .
RUN npm run build

FROM nginx:1.27-alpine
ENV API_UPSTREAM=https://vinh-khanh-food-tour-f2ddbxcgbabmfehr.eastasia-01.azurewebsites.net
COPY nginx/default.conf /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
