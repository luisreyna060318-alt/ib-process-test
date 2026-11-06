<!-- GENERADO desde entregables/UX_Manual_Pantallas_ExpoReturn_v0.3.docx el 2026-11-06 con tools/docx/docx_to_md.py. La fuente de verdad es el Word: haga los cambios en el .docx y vuelva a generar. -->

# 01 · Introducción


## Propósito del documento

Este manual describe **cada pantalla** de la aplicación *Expo & Return FG Follow Up*: qué muestra, quién la usa, qué puede hacer cada rol y qué reglas ve el usuario. También es el **registro de control de la experiencia de usuario (UX)**: guarda qué se revisó en cada ronda, qué se corrigió y qué queda pendiente de validar con los usuarios.

Complementa a la *Especificación de Requerimientos de Software (SRS)*, que define las reglas de negocio, el modelo de datos y la arquitectura. Cuando una pantalla aplica una regla, este manual la cita por su código (VAL-xxx, DR-x) sin repetir el detalle.

## Alcance de esta versión

| **Concepto**                         | **Valor**                                                                                                                                                               |
|--------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Versión de la aplicación documentada | **Borrador v0.1.5** (escritorio Windows, .NET 8 / WPF)                                                                                                                  |
| Datos                                | Demostración sintética (nombres ficticios). Puede importar el Excel real en memoria y en sólo lectura                                                                   |
| Base de datos                        | Ninguna en el borrador: los cambios se pierden al cerrar                                                                                                                |
| Usuario                              | Simulado desde la barra superior: cada persona de la matriz Champions VS, con su área y sus VS. En producción vendrá de Windows (Active Directory) y de la misma matriz |
| Capturas                             | Versión v0.1.5, tomadas en Windows a 1920 × 1080 el 06-nov-2026 (cuarta ronda de revisión)                                                                              |

## Cómo leer cada ficha de pantalla

Cada pantalla tiene un identificador **PNT-xx**, que se mantiene en todas las versiones, y una ficha con:

1.  **Captura anotada**: los círculos rojos numerados corresponden a la tabla *Elementos*. En el detalle, el verde ligero marca los campos que el usuario puede editar y el gris los de sólo lectura.

2.  **Propósito** y **usuarios / roles**.

3.  **Elementos**: qué es cada parte numerada y cómo se comporta.

4.  **Acciones** disponibles.

5.  **Reglas visibles** (validaciones, permisos y cálculos que el usuario ve en esa pantalla).

6.  **Estado UX**: resultado de la última revisión y observaciones abiertas, que se detallan en el capítulo 06.

## Cómo se mantiene

- La **fuente de verdad es este Word**. Los comentarios y el control de cambios se hacen aquí.

- En cada ronda de revisión con usuarios se agregan las capturas nuevas, se actualiza la columna *Estado UX* de las fichas y se registran los hallazgos en el capítulo 06 con su código **UX-xxx**.

- Las capturas originales se guardan por versión en el repositorio (`docs/ux/capturas/<versión>/`). Las coordenadas de los marcadores están en `tools/ux/markers_<versión>.json`; las capturas anotadas se regeneran con `tools/ux/annotate_screens.py` y se insertan en este Word.
