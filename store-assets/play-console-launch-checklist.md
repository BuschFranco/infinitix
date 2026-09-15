# Infinitix — Checklist final para publicar (cuenta nueva de Play Console)

Todo lo de contenido (AAB, textos, imágenes, formularios) ya está listo. Lo que queda es
cargarlo en Play Console y correr la prueba cerrada obligatoria. Orden recomendado:

## 1. Crear la app en Play Console
- Nombre: Infinitix
- Idioma predeterminado: Español (Argentina) — agregar English (US) como idioma adicional
- Tipo: App / Juego, Gratis

## 2. Cargar la ficha principal (Main store listing)
- Título y descripciones: copiar de [listing-copy.md](listing-copy.md) (ES y EN)
- Ícono: `icon-512.png`
- Gráfico destacado (feature graphic): `feature-graphic-1024x500.png`
- Capturas de pantalla (teléfono): las 5 de `marketing/screenshot-1.png` a `screenshot-5.png`
- Categoría: Juegos → Arcade
- Datos de contacto: tu email (francobusch130@gmail.com), sitio web (https://buschfranco.github.io/infinitix/)
- Política de privacidad: la URL de tu página ya publicada (`/privacy/`)

## 3. App content (pestaña "Configuración de contenido")
- **Clasificación de contenido**: cuestionario con las respuestas de
  [content-rating-and-data-safety.md](content-rating-and-data-safety.md)
- **Seguridad de los datos**: mismas respuestas del mismo archivo ("No recopila datos")
- **Anuncios**: declarar que la app NO contiene anuncios
- **Público objetivo y contenido**: seleccioná el rango de edad que corresponda (sin
  contenido para audiencias específicamente infantiles — es apto para todo público pero no
  está diseñada exclusivamente para niños)
- **Apps del gobierno / COVID-19 / apps de noticias**: no aplica, marcar que no

## 4. Países de distribución
- Ya que no hay barreras de idioma/legal (confirmado que se puede lanzar global sin datos
  sensibles), seleccioná todos los países disponibles.

## 5. Subir el AAB a un track de "Closed testing" (obligatorio, cuenta nueva)
- Play Console → Testing → Closed testing → crear track (ej. "Beta cerrada")
- Subir `build-android/Infinitix.aab` (versionCode 2) ahí, NO directo a producción
- Cargar notas de la versión (podés reusar la descripción corta)

## 6. Conseguir los 12 testers
Esto es lo único que depende de gente externa. Opciones:
- Crear una lista de emails de Gmail (amigos, familia, comunidad) — mínimo 12 cuentas
  distintas de Google.
- O crear un Google Group y agregar esas direcciones ahí, y poner el Group como la lista de
  testers (más fácil de armar rápido).
- Compartís el link de opt-in que te da Play Console; cada tester tiene que:
  1. Abrir el link con su cuenta de Google.
  2. Aceptar ser tester.
  3. Instalar la app desde el link de Play Store que aparece después de aceptar.
  4. Idealmente abrirla al menos una vez (Google a veces pide actividad real, no solo opt-in).

## 7. Esperar 14 días corridos
- Con los 12+ testers activos, Play Console habilita automáticamente el botón para promover
  la versión a producción una vez pasado el período.

## 8. Promover a producción
- Play Console → Production → "Promote release" desde el track de closed testing.
- Enviar a revisión. La primera revisión de una app nueva puede tardar de horas a unos
  pocos días.

---

**Lo único que no puedo hacer por vos**: nada de esto tiene API pública, así que los pasos
2 a 8 son manuales en la interfaz de Play Console. Si querés, te puedo ayudar a redactar el
mensaje de invitación para reclutar a los 12 testers (WhatsApp/grupo/redes).
