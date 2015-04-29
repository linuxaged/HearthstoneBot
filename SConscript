import os

# Get an instance to our environment
env = Environment()

# Enable automatic help generation
#SConscript('conf/scons/Scons_Help_script.py', exports = ['env'])
# Help header and footer
#env.jHelpHead("The following targets are supported;")
#env.jHelpFoot("For additional information refer to the webpage.")

# Include helpers
SConscript('conf/scons/Scons_DirectoryHelper_script.py', exports = ['env'])
SConscript('conf/scons/builders/c_sharp.py', exports = ['env'])

env['CSC'] = "\"D:/Program Files/Unity455/Editor/Data/Mono/bin/gmcs.bat\""
env['CSCWIN'] = False
env['CSCFLAGS'] = " -debug "

# Start searching in project folder
SConscript('projects/SConscript', variant_dir = "build", exports = ['env'], duplicate=0)

# Set the default target (compile the kernel, make image, run it)
env.Default('projects/Release-ZIP')

# Generate the help target
#env.jGenHelp()
