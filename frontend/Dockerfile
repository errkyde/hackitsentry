# syntax=docker/dockerfile:1
FROM node:22-alpine AS build
WORKDIR /app
COPY frontend/package*.json ./
RUN --mount=type=cache,id=npm-sentry,target=/root/.npm \
    npm ci
COPY frontend/ .
RUN npm run build

FROM nginx:alpine AS final
RUN apk upgrade --no-cache zlib
COPY --from=build /app/dist /usr/share/nginx/html
COPY frontend/nginx.conf /etc/nginx/templates/default.conf.template
EXPOSE 80
