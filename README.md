# CustomDependencyManager
 Alternative lightweight dependency manager for Android and iOS.

Uses OdinInspector for the GlobalConfig scriptable object.

Dependencies are only searched on build, preprocess for Android, postprocess for iOS. For Android, it will only edit the mainTemplate.gradle file. On iOS, it will create the Podfile and install them into the xcode project. All other options that you want should be implemented manually, i.e. editing gradleTemplate.properties to enable jetifier or androidX.
