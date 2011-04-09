#!/bin/bash
# I've been using F# on Mono, so having Bash and so on makes sense...

EXE=Ninlil.exe
if [ -f "$EXE" ]
then
   mv "$EXE" "$EXE.old"
fi

fsharpc --nologo --optimize --out:"$EXE" Tumblr.fs Ninlil.fs
