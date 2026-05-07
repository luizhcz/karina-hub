# Single-stage nginx — após cleanup React (Fase 5), o app é HTML/CSS/JS
# vanilla servido estaticamente. Sem Node, sem npm, sem build step.
#
# nginx.conf usa BACKEND_URL como template substituído em runtime via envsubst
# (igual ao frontend principal).
#
# Pra regerar mvp/public/css/utilities.css quando o design mudar, rode
# `bash tools/build-css.sh` localmente — output é versionado em git.

FROM nginx:1.27-alpine

COPY public/ /usr/share/nginx/html

# Template processado automaticamente pelo nginx:alpine no startup
COPY nginx.conf /etc/nginx/templates/default.conf.template

EXPOSE 80

# Aponta pro service backend dentro da rede do compose
ENV BACKEND_URL=http://backend:5189
