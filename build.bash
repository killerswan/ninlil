#!/bin/bash
# I've been using F# on Mono, so having Bash and so on makes sense...

EXE=Ninlil.exe
if [ -f "$EXE" ]
then
   mv "$EXE" "$EXE.old"
fi

#fsharpc --nologo --optimize --out:"$EXE" HTTP.fs Tumblr.fs Ninlil.fs
fsc --warn:4 --nologo --optimize --out:"$EXE" HTTP.fs Tumblr.fs Ninlil.fs
