# LunchOrganizer — Guide de l'utilisateur

Ce guide s'adresse aux personnes qui utilisent LunchOrganizer au quotidien. Aucune connaissance technique n'est nécessaire.

L'application s'ouvre en français. Chaque bouton est indiqué en **français *(anglais)*** afin que vous puissiez suivre dans les deux langues — le sélecteur **FR / EN** en haut à droite change la langue à tout moment, et votre choix est mémorisé sur cet ordinateur.

- La **partie 1 — Réserver son repas** concerne tout le monde.
- La **partie 2 — Administration** concerne la personne qui gère les menus, les prix et les employés.

> L'installation et le déplacement de l'application sont traités dans [`INSTALLATION.md`](INSTALLATION.md) (en anglais), pas ici.
>
> *Une version anglaise de ce guide est disponible : [`USER_GUIDE.md`](USER_GUIDE.md).*

---

## La seule règle vraiment importante

> ### Le repas du jour peut être réservé ou modifié jusqu'à **09h00**.
>
> Passé 09h00, la case du jour se verrouille et ne peut plus être modifiée — la cuisine a déjà reçu la liste. Le lendemain et le reste de la semaine restent ouverts normalement.

Tout le reste de ce guide n'est que du détail. C'est ce point-là qu'il faut retenir.

---

# Partie 1 — Réserver son repas

## Ouvrir l'application

Saisissez dans un navigateur l'adresse que votre administrateur vous a communiquée. Aucun mot de passe n'est nécessaire et il n'y a rien à installer. Un téléphone ou une tablette conviennent également : en dessous d'une certaine largeur, la grille hebdomadaire s'affiche jour après jour, les uns sous les autres.

## Étape 1 — Choisir la semaine

Deux grands boutons figurent en haut de la page :

| Bouton | Affiche |
|---|---|
| **Cette semaine** *(This week)* | Le lundi–vendredi en cours |
| **Semaine prochaine** *(Next week)* | Le lundi–vendredi suivant |

Le titre indique la semaine en toutes lettres — *« Semaine du lundi 17 août »* — plutôt qu'un numéro de semaine. Une petite flèche à côté des boutons permet d'atteindre les semaines suivantes. Sur la semaine en cours, il n'y a pas de flèche « précédent » : les semaines passées ne peuvent pas être réservées.

Seuls les jours du lundi au vendredi apparaissent. Il n'y a pas de repas le week-end.

## Étape 2 — Saisir votre nom

Dans le champ **Votre nom** *(Your name)*, commencez à écrire. Après **deux lettres**, une liste de noms correspondants apparaît ; sélectionnez le vôtre à la souris, ou avec les flèches du clavier puis Entrée.

Les accents n'ont pas d'importance — saisir `chloe` trouve *Chloé Bernard* — et les majuscules non plus.

**Si votre nom ne figure pas dans la liste**, l'application propose *« Enregistrer «votre nom» comme nouvel employé »*. Une seule confirmation suffit à vous ajouter, sans passer par un administrateur. Si des noms proches existent déjà, ils sont affichés en premier : merci de vérifier avant de confirmer. **Jean Dupont** et **Jean Dupond** sont deux personnes différentes, et la cuisine cuisinera pour les deux.

## Étape 3 — Lire l'état du jour

Dès que l'application sait qui vous êtes, un bandeau résume la journée, par exemple :

> **Aujourd'hui (lun. 17 août) : Menu 2 réservé** · modifiable encore 1 h 12 min

ou

> **Aucun repas réservé pour aujourd'hui** · vous pouvez encore réserver pendant 1 h 12 min

Une fois 09h00 passé, le bandeau l'indique et la colonne du jour est verrouillée.

## Étape 4 — Choisir vos menus

La grille reprend la feuille papier, à l'écran : **une colonne par jour, une ligne par menu.** Chaque jour a ses propres menus, ses propres descriptions et son propre prix — le *Menu 2* du lundi et le *Menu 2* du mardi sont deux plats différents.

- Cliquez sur une case pour réserver ce menu ce jour-là.
- Cliquez à nouveau sur la case sélectionnée pour supprimer le repas de ce jour.
- **Un seul repas par personne et par jour.** Choisir un second menu le même jour remplace le premier, il ne s'y ajoute pas.
- Les cases grisées ne sont pas utilisables. Survolez-les et l'application explique pourquoi : le jour est passé, ou 09h00 est dépassé.
- Un jour pour lequel aucun menu n'a encore été publié n'affiche simplement rien à choisir.

### Réserver toute la semaine d'un coup

Sous la grille, choisissez un numéro de menu puis cliquez sur **Appliquer le menu N à tous les jours ouverts** *(Apply Menu N to every open day)*. L'application remplit tous les jours encore ouverts, puis indique exactement ce qu'elle a fait : les jours appliqués, et les jours ignorés avec la raison (déjà passé, heure limite dépassée, ou menu non proposé ce jour-là). Rien n'est masqué.

## Étape 5 — Enregistrer

Rien n'est réservé tant que vous n'avez pas cliqué sur **Réserver mes repas** *(Book my lunches)*. Un seul clic enregistre toute la semaine.

Une confirmation verte apparaît, puis la grille se recharge depuis la base de données : ce que vous voyez ensuite correspond toujours à ce qui est réellement enregistré, et non simplement à ce que vous avez cliqué.

## Modifier ou annuler

Revenez quand vous voulez, sélectionnez votre nom et modifiez les cases comme précédemment. Les règles sont les mêmes : aujourd'hui jusqu'à 09h00, les jours suivants librement, les jours passés jamais.

**Pour annuler un repas**, cliquez sur la case sélectionnée pour la vider, puis enregistrez. Sans enregistrement, rien ne change.

## Imprimer

La page de réservation et le rapport d'administration s'impriment proprement — utilisez la commande d'impression habituelle de votre navigateur. La navigation et les boutons sont retirés automatiquement, et le titre reprend la semaine ainsi que la date d'impression.

---

# Partie 2 — Administration

## Se connecter

Ouvrez **Administration** dans la navigation en haut de page. Les comptes administrateurs se trouvent dans un petit fichier (`config/admin-users.json`) qu'un administrateur peut éditer avec Notepad++ ; il n'y a pas d'écran de création de compte. Une session dure 8 heures.

Après cinq tentatives échouées en une minute, la connexion se met brièvement en pause. Patientez un instant et réessayez.

L'espace d'administration comporte trois onglets.

## Onglet Menus et prix

C'est l'onglet le plus utilisé, en général une fois par semaine.

**Les menus.** Choisissez une semaine, puis pour chaque jour ajoutez des menus avec **Ajouter un menu** *(Add a menu)* et saisissez une description — *« Poulet au curry, riz basmati »*. Les menus sont numérotés automatiquement à partir de 1. Un menu appartient à **une date précise** : configurer le lundi ne dit rien du mardi. Le bouton **Copier cette description sur toute la semaine** *(Copy this description across the whole week)* évite de ressaisir un plat proposé plusieurs jours de suite.

**Les prix.** Chaque jour a un prix unique, commun à tous les menus de ce jour. Définissez-le jour par jour, ou utilisez **Appliquer ce prix à toute la semaine** *(Apply this price to the whole week)*. Les nouveaux jours reprennent le prix par défaut défini dans `config/app.json` (actuellement **CHF 12.50**).

> **Modifier un prix ne réécrit jamais le passé.** Chaque réservation conserve le prix en vigueur au moment où elle a été faite. Si le prix du mardi change après que dix personnes ont réservé, ces dix personnes gardent leur prix et le rapport reste juste. Un changement de prix ne vaut que pour l'avenir.

**La suppression d'un menu** est refusée tant que quelqu'un l'a réservé — l'application indique combien de réservations existent. Il faut d'abord supprimer ou déplacer ces réservations. C'est volontaire : il devient impossible de laisser quelqu'un avec un repas qui n'existe plus.

## Onglet Employés

La liste de toutes les personnes enregistrées, avec recherche. Vous pouvez ajouter quelqu'un manuellement et modifier un nom ou une adresse e-mail directement dans la liste.

**Supprimer ou désactiver.** Une personne qui n'a jamais réservé peut être supprimée définitivement. Une personne ayant un historique de réservations **ne le peut pas** : la supprimer fausserait les rapports passés. L'application propose alors **Désactiver** *(Deactivate)* : la personne n'apparaît plus dans la saisie de nom, mais son historique reste intact pour les rapports. C'est le bon choix pour quelqu'un qui a quitté l'entreprise.

## Onglet Rapport

Choisissez un employé ou *Tous les employés* *(All employees)*, définissez une date de début et de fin, et le tableau liste toutes les réservations — **Date · Jour · Menu · Description · Prix** — avec le **total de la période** dans une carte de synthèse. C'est ce qui sert à facturer ou à contrôler.

**Exporter en CSV** *(Export to CSV)* télécharge le même tableau pour Excel. Le fichier est en UTF-8 avec marque d'ordre des octets, de sorte que les noms accentués s'ouvrent correctement dans Excel, sans aucune étape d'importation.

---

# L'e-mail quotidien

Chaque jour ouvrable à **09h01**, une minute après l'heure limite, LunchOrganizer envoie à la cuisine le récapitulatif des repas du jour.

L'e-mail est groupé par menu, chacun avec sa description et la liste alphabétique des personnes l'ayant choisi, ainsi qu'un total en tête. Les menus que personne n'a choisis sont omis. Il est envoyé en français par défaut, indépendamment de la langue que vous utilisez personnellement sur le site.

**Si personne n'a réservé, rien n'est envoyé.** La cuisine ne reçoit jamais de liste vide.

Quelques points utiles :

- L'e-mail est envoyé par un petit programme planifié, et **non** par le site. Il part donc même si le site a été redémarré ou n'est pas en fonctionnement.
- Le récapitulatif **ne peut jamais être envoyé deux fois** pour la même journée, même si deux copies du planificateur s'exécutent en même temps. La première qui réserve la journée l'emporte.
- L'envoi à 09h01 plutôt qu'à 09h00 est volontaire : cela garantit qu'une réservation enregistrée à 08h59min59s figure bien dans l'e-mail.

---

# Questions fréquentes

**J'ai réservé mais je ne suis pas dans l'e-mail.**
Dans la quasi-totalité des cas, la réservation a été enregistrée après 09h00 et compte donc pour un jour suivant, pas pour aujourd'hui. Ouvrez la semaine et vérifiez quelle case est sélectionnée.

**Quelqu'un a enregistré mon nom deux fois, avec une légère différence.**
Un administrateur peut désactiver le mauvais dans l'onglet Employés. L'application empêche déjà les doublons exacts — y compris les différences de majuscules ou d'accents, si bien que `ALICE MARTIN` ne peut pas coexister avec `Alice Martin` — mais elle ne peut pas deviner que *Jean Dupond* voulait dire *Jean Dupont*.

**J'ai oublié de réserver et il est 09h30.**
L'application ne peut rien faire : la liste est partie en cuisine. Adressez-vous directement à eux.

**Puis-je réserver pour un collègue ?**
Oui. Saisissez son nom à la place du vôtre. La page de réservation n'a pas de mot de passe. Elle repose sur la confiance, exactement comme la feuille papier qu'elle remplace.

**Puis-je réserver plusieurs semaines à l'avance ?**
Oui, aussi loin que les menus ont été publiés. Les jours sans menu ne peuvent pas être réservés.

**La page indique qu'elle se reconnecte.**
Le site maintient une connexion permanente avec le serveur. En cas de coupure réseau, il se reconnecte tout seul et propose un bouton de reprise s'il n'y parvient pas. Vos réservations enregistrées ne sont jamais affectées ; seuls des clics non enregistrés seraient perdus.

**J'ai changé de langue et mes réservations ont disparu.**
Elles sont toujours là. Changer de langue recharge la page : ressaisissez votre nom et la grille réapparaît telle qu'elle était.

---

*Les questions auxquelles ce guide ne répond pas sont probablement traitées dans [`DEBUGGING.md`](DEBUGGING.md) (faire fonctionner l'application) ou [`INSTALLATION.md`](INSTALLATION.md) (l'installer et la configurer). Ces deux documents sont en anglais.*
