// Copyright (c) Kevin Cantu <me@kevincantu.org>
//
// redux - manipulate old Tumblr posts
///////////////////////////////////////////////

open System.Collections.Generic
open System.Net
open System.IO
open System.Threading
open System.Text.RegularExpressions
open System.Xml
open System.Drawing


// command line args
let cmdline    = System.Environment.GetCommandLineArgs()
let blog       = cmdline.[1]
let api        = "http://" + blog + ".tumblr.com/api" 
let email      = cmdline.[2]
let password   = cmdline.[3]





///////////////////////////////////////////////
// FETCH A URL

let getDocRaw (url:string) = 
   let getpage (url:string) = 
      async {
         // see Expert F# at 383, etc.
         let req        = WebRequest.Create(url, Timeout=5)
         use! response  = req.AsyncGetResponse()
         use reader     = new StreamReader(response.GetResponseStream())
         return reader.ReadToEnd()
      }

   // get data as XML
   let page = Async.RunSynchronously(getpage url)

   (page)



///////////////////////////////////////////////
// SIMPLE QUERIES

// xml out
let readPosts start num = getDocRaw <| api + "/read" +
                                                   "?start=" + start + 
                                                   "&num="   + num +
                                                   "&type="  + "photo"

// status out
let deletePosts id       = getDocRaw <| api + "/delete" +
                                                   "?email="      + email + 
                                                   "&password="   + password + 
                                                   "&post-id="    + id
                     
// new id out
let reblogPosts id rkey  = getDocRaw <| api + "/reblog" + 
                                                   "?email="      + email + 
                                                   "&password="   + password + 
                                                   "&post-id="    + id + 
                                                   "&reblog-key=" + rkey



///////////////////////////////////////////////
// RESULT PROCESSING

// after getPosts
let processPosts postsXML =
   let doc = new XmlDocument()
   postsXML |> doc.LoadXml // so doc is mutable?

   // add prettier printing
   //fsi.AddPrinter( fun (x:XmlNode) -> x.OuterXml );;

   // process the data
   let tumblr = doc.ChildNodes.Item(1)
   let posts  = tumblr.ChildNodes.Item(1)

   // overall statistics
   let start = posts.Attributes.GetNamedItem("start").Value
   let total = posts.Attributes.GetNamedItem("total").Value

   let postsFound = 
      [
         for ii in 0..(num-1) do
            let post       = posts.ChildNodes.Item(ii)
            let id         = post.Attributes.GetNamedItem("id").Value
            let reblogkey  = post.Attributes.GetNamedItem("reblog-key").Value
            let date       = post.Attributes.GetNamedItem("date-gmt").Value
            yield (id, reblogkey, date, post)
      ]

   // display a post tuple
   let display (id, reblogkey, date, post:XmlNode) = 
      let pic = post.ChildNodes.Item(1).InnerText
      printfn "id: %s, %s, %s -> %s" id reblogkey date pic

   // print stats
   printfn "start: %s/%s" start total

   // print all
   postsFound |> List.map display |> ignore

   (start, total, postsFound)


