# Changelog

## [0.8.0](https://github.com/chemodun/X4LogWatcher/compare/v0.7.1...v0.8.0) (2025-07-19)


### Features
* Implement possibility to handle log entries that span multiple lines, making it easier to read and analyze complex log messages ([bc1feeb](https://github.com/chemodun/X4LogWatcher/commit/bc1feeb634b47d472b7cd52400bb7b530bb3e014))

### Documentation

* **bbcode:** Update bbcode files ([aa67dcf](https://github.com/chemodun/X4LogWatcher/commit/aa67dcf29230fcba9decb02c93a7304daf3696a2))

## [0.7.1](https://github.com/chemodun/X4LogWatcher/compare/v0.7.0...v0.7.1) (2025-06-12)


### Code Refactoring

* extract file change processing into a separate method and apply it on the both modes: "usual" and forced ([554da56](https://github.com/chemodun/X4LogWatcher/commit/554da56ea4a81dc0ccd3e794560751fb4b2f91d1))
* remove default initialization for boolean fields and improve config serialization ([6be9770](https://github.com/chemodun/X4LogWatcher/commit/6be97701f22d76127f82b004a07e10e3761c0b4a))
* remove default initialization for boolean fields in MainWindow ([3175ee8](https://github.com/chemodun/X4LogWatcher/commit/3175ee8af7e79ab552b7ea218ebcb05ed249d985))
* unify log file processing for standard and forced refresh modes ([f24e8d8](https://github.com/chemodun/X4LogWatcher/commit/f24e8d82e9c3e0d62e50d568c04c8793fc8cb202))


### Documentation

* **bbcode:** Update bbcode files ([a64cd80](https://github.com/chemodun/X4LogWatcher/commit/a64cd805537348ccdb4367a8b303cc4512ffda49))
* **bbcode:** Update bbcode files ([8b98ead](https://github.com/chemodun/X4LogWatcher/commit/8b98ead5d7672a29fb7c95377ef7bddde0dc1441))
* **bbcode:** Update bbcode files ([fdb7578](https://github.com/chemodun/X4LogWatcher/commit/fdb75789a3f4ae669e59b447176ec07d203e4809))
* fix wording in changelog for version 0.7.0 ([ed7194d](https://github.com/chemodun/X4LogWatcher/commit/ed7194d9172a8c21565497dfb1ec80cc6a5f56e5))
* update changelog for version 0.7.0 with fixes for memory leaks and focus issues ([f1c496d](https://github.com/chemodun/X4LogWatcher/commit/f1c496dab5f1edc9be5bb5a0c4ce1e6814061271))
* update changelog for version 0.7.0 with memory leak fix and attribution ([fd0be13](https://github.com/chemodun/X4LogWatcher/commit/fd0be13d4d1c2f8c4d132524fddafbd7b9b9c0ab))

## [0.7.0](https://github.com/chemodun/X4LogWatcher/compare/v0.6.0...v0.7.0) (2025-06-12)


### Features

* implement virtual content management for large files in TabInfo ([3a50975](https://github.com/chemodun/X4LogWatcher/commit/3a509759d402cd1b424def10cfe0a919ab2c7ade))


### Bug Fixes

* add resource cleanup on window closing ([a44ae0a](https://github.com/chemodun/X4LogWatcher/commit/a44ae0a93d27d28e0c5a6d8ec97254202eb4a5bb))
* implement IDisposable for TabInfo ([868195f](https://github.com/chemodun/X4LogWatcher/commit/868195f3554b14c7349ca49228cb3d24365599ea))
* prevent focus shift when selecting auto-created tabs ([18f59a3](https://github.com/chemodun/X4LogWatcher/commit/18f59a347502c5605da9b6bb4076c7f6609055da))


### Code Refactoring

* enhance auto-scrolling behavior and add scroll detection for tabs ([b71e2ce](https://github.com/chemodun/X4LogWatcher/commit/b71e2ce7757fc8f45a3bf42250612085a6c46454))


### Documentation

* **bbcode:** Update bbcode files ([6b1e77c](https://github.com/chemodun/X4LogWatcher/commit/6b1e77c465617b58e27697469a6a8dc0c7620980))
* **bbcode:** Update bbcode files ([32b31d7](https://github.com/chemodun/X4LogWatcher/commit/32b31d79d6924741bfe6556296900e60cd51c5ee))
* update README for clarity and correct demo link ([3e15f94](https://github.com/chemodun/X4LogWatcher/commit/3e15f945e55474a78e6cee8811c69eaf8dd547fd))


### Reverts

* "feat: implement virtual content management for large files in TabInfo" ([d3e2cf9](https://github.com/chemodun/X4LogWatcher/commit/d3e2cf9a421b3cd768c11cf7e6c38c56077a3a07))

## [0.6.0](https://github.com/chemodun/X4LogWatcher/compare/v0.5.0...v0.6.0) (2025-05-13)


### Features

* preliminary implement auto tab configuration feature, currently without UI, only logic, for dynamic tab creation ([2cbe2c2](https://github.com/chemodun/X4LogWatcher/commit/2cbe2c2c673bfaa211f4b9c967894524300af77b))


### Bug Fixes

* AutoTab cleanup on profile loading ([9357c58](https://github.com/chemodun/X4LogWatcher/commit/9357c58f8b73cf9cebebdb560419ecfa78bf2ce8))


### Code Refactoring

* adjust header font sizes for tab control and add tab button ([e467bdc](https://github.com/chemodun/X4LogWatcher/commit/e467bdc18514805781d2a1d3dc27812812bbce88))
* adjust layout and improve help text in AutoTabConfigDialog ([9357c58](https://github.com/chemodun/X4LogWatcher/commit/9357c58f8b73cf9cebebdb560419ecfa78bf2ce8))
* remove MenuAutoTabs_Click handler implementation ([fcde9f2](https://github.com/chemodun/X4LogWatcher/commit/fcde9f24828795ed7089487099f0e9c12986c9cb))
* update tab naming and indicators for auto-created tabs ([dd1287c](https://github.com/chemodun/X4LogWatcher/commit/dd1287cae30a423ac0a81a71a09a76c26fcea053))
* use named group for unique part of AutTab regex ([20f8422](https://github.com/chemodun/X4LogWatcher/commit/20f842236944f573de0c2f49dfc8064cb7ce7884))


### Documentation

* add images for AutoTabs feature and update related documentation ([4ed82a3](https://github.com/chemodun/X4LogWatcher/commit/4ed82a3a4bf69b5c37ecd24250035849851c5868))

## [0.5.0](https://github.com/chemodun/X4LogWatcher/compare/v0.4.1...v0.5.0) (2025-04-12)


### Features

* add options for skipping signature errors and real-time stamping ([76bc027](https://github.com/chemodun/X4LogWatcher/commit/76bc0270a15ee2ac17c275ecb10b9b0a5af46a7b))
* update tab header font weight based on new content status ([2304e37](https://github.com/chemodun/X4LogWatcher/commit/2304e37d8399e1973a09e1698107c9082b4d4a92))


### Code Refactoring

* adjust tab control header styles to slightly smaller font size ([eaa38d1](https://github.com/chemodun/X4LogWatcher/commit/eaa38d188586e228d315fd32effbe2c7f640c2ad))


### Documentation

* **bbcode:** Update bbcode files ([37b7101](https://github.com/chemodun/X4LogWatcher/commit/37b7101e3720fd45ff43062720628a0c10849739))
* update README - fix mistypings ([f76ef43](https://github.com/chemodun/X4LogWatcher/commit/f76ef435b46360c434c66c7cdd8512a4d3f605f3))

## [0.4.1](https://github.com/chemodun/X4LogWatcher/compare/v0.4.0...v0.4.1) (2025-04-11)


### Bug Fixes

* content reread after active tab was paused/started (i.e. Enabled is unchecked/checked) and not switched in between ([b89b6b2](https://github.com/chemodun/X4LogWatcher/commit/b89b6b2194ff94e099df3344b43dc9e3b31b9809))
* old value of regex is used after regex edit and Enables is checked ([b89b6b2](https://github.com/chemodun/X4LogWatcher/commit/b89b6b2194ff94e099df3344b43dc9e3b31b9809))


### Documentation

* **bbcode:** Update bbcode files ([01dc23f](https://github.com/chemodun/X4LogWatcher/commit/01dc23f54b204cb539f95fb1361b0c3f0d234566))
* **bbcode:** Update bbcode files ([18bd899](https://github.com/chemodun/X4LogWatcher/commit/18bd899144e924bd9e925ef5c1d03f3038c2b992))
* update README - fix mistypings ([6547fd4](https://github.com/chemodun/X4LogWatcher/commit/6547fd4188a4db78ad2428b69f34e82f64390d1a))

## [0.4.0](https://github.com/chemodun/X4LogWatcher/compare/v0.3.0...v0.4.0) (2025-04-11)


### Features

* add configurable log file extension support ([cfd0d6b](https://github.com/chemodun/X4LogWatcher/commit/cfd0d6b10daf007072243cd228bc0d52e1602133))
* new content indicators for tabs ([dcbe58b](https://github.com/chemodun/X4LogWatcher/commit/dcbe58be03bb113499f3b5afb51071f856b72e17))
* new parameter After Lines count, defined how many lines is shown after matched line ([b2d2b80](https://github.com/chemodun/X4LogWatcher/commit/b2d2b802e49e45922b167b32ec3857b95ea6f430))


### Code Refactoring

* fixed "tab" order for input elements ([b2d2b80](https://github.com/chemodun/X4LogWatcher/commit/b2d2b802e49e45922b167b32ec3857b95ea6f430))
* replace dictionary initializations with array syntax for tabs and search results ([87e7152](https://github.com/chemodun/X4LogWatcher/commit/87e715282c450423f02951490596b97c9c00cc5b))
* tab items edits processing logic ([2cd2063](https://github.com/chemodun/X4LogWatcher/commit/2cd2063d7a8219309237a149ff3681788d73350b))

## [0.3.0](https://github.com/chemodun/X4LogWatcher/compare/v0.2.0...v0.3.0) (2025-04-09)


### Features

* add progress reporting for file loading in status bar ([58574b3](https://github.com/chemodun/X4LogWatcher/commit/58574b3360cafdfd60caded10091ea424e27e1ee))
* add search in filtered content functionality with find panel in MainWindow ([66e88b5](https://github.com/chemodun/X4LogWatcher/commit/66e88b573ba72ec4491fea44cb8e5006af36c94e))
* Basic status bar is implemented ([6ca3570](https://github.com/chemodun/X4LogWatcher/commit/6ca3570a3b9bcdb4f41337e65edfc71a86ece9fe))


### Bug Fixes

* ensure status bar updates are processed on the UI thread ([e1b0dde](https://github.com/chemodun/X4LogWatcher/commit/e1b0dde8c04d0d2a1aa4281b64eb8c10e0be1ef5))


### Code Refactoring

* add Enter key handling for search navigation in content box ([44847ae](https://github.com/chemodun/X4LogWatcher/commit/44847aef57c49827b28fa3d7f536c2479951f8f2))
* enhance tab content processing with "parallel" execution  - i.e. changed lines is read once and then processed against each filter on enabled tabs ([cb80707](https://github.com/chemodun/X4LogWatcher/commit/cb8070782254078b9539f83f94c17a9ab187df5b))


### Documentation

* add demo video link to README.md ([37f8ad5](https://github.com/chemodun/X4LogWatcher/commit/37f8ad533a3b6e1849b669e4c22445951ad6a30c))
* **bbcode:** Update bbcode files ([aa42524](https://github.com/chemodun/X4LogWatcher/commit/aa4252487accbb2b4c78c64365303eb19f692a74))
* **bbcode:** Update bbcode files ([d771dd0](https://github.com/chemodun/X4LogWatcher/commit/d771dd0ff6a82e4f64f7ef9029e8cabb9ae620a0))
* **bbcode:** Update bbcode files ([c76e4eb](https://github.com/chemodun/X4LogWatcher/commit/c76e4ebcd13f410b7c85dc0d411ff60cc9cedb72))
* **bbcode:** Update bbcode files ([d203fa0](https://github.com/chemodun/X4LogWatcher/commit/d203fa07ebca709975ba9a7ce575f46b81161827))
* format links section in README.md for consistency ([ecf62a4](https://github.com/chemodun/X4LogWatcher/commit/ecf62a41ca5afeb0ced656b519923b4be9c59a4a))
* improve installation instructions in README.md for clarity ([60fa8e9](https://github.com/chemodun/X4LogWatcher/commit/60fa8e92a7b05b67ccd435da0c20fd25454eadca))
* update the README.md to be comply with the latest version ([441ffa3](https://github.com/chemodun/X4LogWatcher/commit/441ffa3648cd56bc51a407f4de070c1281e77323))

## [0.2.0](https://github.com/chemodun/X4LogWatcher/compare/v0.1.0...v0.2.0) (2025-04-08)


### Features

* add Forced Refresh menu item and functionality ([1bdc0f1](https://github.com/chemodun/X4LogWatcher/commit/1bdc0f126f595ff9d7a1564cbb72468bc089da02))


### Bug Fixes

* title for File Watch ([3482c94](https://github.com/chemodun/X4LogWatcher/commit/3482c945ab1a35cede864ebf35be8bdb27c55ee5))


### Code Refactoring

* simplify regex match handling in log processing ([1c202c5](https://github.com/chemodun/X4LogWatcher/commit/1c202c59c2ab824961907bd2e663348491eda52a))
* update TabInfo and adjust related logic ([98a0e82](https://github.com/chemodun/X4LogWatcher/commit/98a0e8223825d3b12d6464fc248ddbdb32877fb3))


### Miscellaneous Chores

* add application icon and update project configuration ([4a26169](https://github.com/chemodun/X4LogWatcher/commit/4a2616907f7e7d81c3bcda77fa78420a418caf0a))
* formatting improvement ([388038a](https://github.com/chemodun/X4LogWatcher/commit/388038a86ed35ba0f92d6346aaffa1ff82a57a7d))
* initial release with basic functionality ([9a10e5c](https://github.com/chemodun/X4LogWatcher/commit/9a10e5c7527a912e4afa1a2de6cdd0c4e359bb94))


### Documentation

* Added README.md, logo and screenshots ([4280327](https://github.com/chemodun/X4LogWatcher/commit/4280327ec03e4eaab96d7b960b977034a09c7670))
* update README.md to clarify features and known issues ([50eb716](https://github.com/chemodun/X4LogWatcher/commit/50eb716e351f561f4fb91de6b993826aa5d10742))
